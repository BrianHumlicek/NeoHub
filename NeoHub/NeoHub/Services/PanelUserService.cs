using System.Diagnostics;
using DSC.TLink.ITv2;
using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.ITv2.MediatR;
using DSC.TLink.ITv2.Messages;
using MediatR;

namespace NeoHub.Services
{
    public class PanelUserService : IPanelUserService
    {
        private readonly IMediator _mediator;
        private readonly IPanelStateService _panelState;
        private readonly ILogger<PanelUserService> _logger;

        /// <summary>
        /// How long to wait for programming mode confirmation (LeadIn) after ConfigurationEnter.
        /// </summary>
        private static readonly TimeSpan ProgrammingModeTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Conservative max ITv2 message payload in bytes.
        /// </summary>
        private const int MaxPayloadBytes = 255;

        /// <summary>
        /// Fixed header overhead per batch response (Start CI + Count CI + DataWidth/BCDLen).
        /// </summary>
        private const int BatchHeaderOverhead = 5;

        /// <summary>
        /// Max labels to request per batch via NotificationLabelText.
        /// Labels are ~28 bytes each (14 chars × 2 bytes BigEndianUnicode) + ~6 bytes header.
        /// </summary>
        private static readonly int MaxLabelsPerRequest = MaxRecordsPerBatch(28, headerOverhead: 6);

        public PanelUserService(
            IMediator mediator,
            IPanelStateService panelState,
            IPanelAccessCodeService accessCodes,
            ILogger<PanelUserService> logger)
        {
            _mediator = mediator;
            _panelState = panelState;
            _accessCodes = accessCodes;
            _logger = logger;
        }

        private readonly IPanelAccessCodeService _accessCodes;

        public async Task<PanelUserReadResult> ReadAllAsync(string sessionId, string masterCode, CancellationToken ct)
        {
            var session = _panelState.GetSession(sessionId);
            if (session == null)
                return new PanelUserReadResult(false, "Session not found");

            if (session.MaxUsers <= 0)
                return new PanelUserReadResult(false, "Panel reported zero users");

            if (string.IsNullOrWhiteSpace(masterCode))
                return new PanelUserReadResult(false, "Master code is required");

            _logger.LogInformation("Reading panel users for session {SessionId}, max users: {MaxUsers}", sessionId, session.MaxUsers);

            _panelState.UpdateSession(sessionId, s =>
            {
                s.IsReadingUsers = true;
                s.UserReadCurrent = 0;
                s.UserReadTotal = s.MaxUsers;
                s.UserReadProgress = null;
            });

            try
            {
                // 1. Read user labels first (no programming mode needed, uses CommandRequestMessage)
                UpdateProgress(sessionId, "Reading user labels…");
                var labels = await ReadUserLabelsAsync(sessionId, session.MaxUsers, ct);

                // 2. Enter programming mode via the access code service (owns the shared lock +
                //    LeadIn wait + exit on all exit paths). The delegate runs with the panel
                //    already in AccessCodeProgramming mode.
                UpdateProgress(sessionId, "Entering programming mode…");
                var scoped = await _accessCodes.ExecuteAsync(
                    sessionId, PanelAccessCodeKind.Master, masterCode, readWrite: false,
                    operation: innerCt => ReadAllUsersAsync(sessionId, session, labels, innerCt),
                    ct);

                if (!scoped.Success)
                {
                    return new PanelUserReadResult(false,
                        scoped.ErrorMessage ?? "Failed to enter programming mode — verify the master code is correct");
                }

                return scoped.Result ?? new PanelUserReadResult(false, "Read returned no result");
            }
            finally
            {
                _panelState.UpdateSession(sessionId, s =>
                {
                    s.IsReadingUsers = false;
                    s.UserReadProgress = null;
                    s.UserReadCurrent = 0;
                    s.UserReadTotal = 0;
                });
            }
        }


        public async Task<PanelUserWriteResult> WriteUserAsync(
            string sessionId, Models.PanelUserState user, Models.PanelUserState original, string masterCode, CancellationToken ct)
        {
            // Detect enable/disable crossings. The panel has side-effects when the access code
            // crosses the enabled/disabled threshold (attributes and partitions get reset by the
            // panel itself). Rather than guess at the panel's defaults we only write the code,
            // then re-read the authoritative state after. This makes the crossing a two-step
            // UX: save the code first, then optionally make further edits.
            bool wasDisabled = Models.PanelUserState.IsDisabledCode(original.CodeValue);
            bool nowDisabled = Models.PanelUserState.IsDisabledCode(user.CodeValue);
            bool crossed = wasDisabled != nowDisabled;

            bool codeDirty = user.CodeValue != original.CodeValue;
            bool attrsDirty = user.Attributes != original.Attributes;
            bool partsDirty = !user.Partitions.SequenceEqual(original.Partitions);
            bool labelDirty = user.UserLabel != original.UserLabel;

            // The panel does not allow writing attributes or partitions for User 1 (master code).
            if (user.UserIndex == 1)
            {
                attrsDirty = false;
                partsDirty = false;
            }

            // On a crossing save, only the access code is written. All other dirty edits are
            // ignored so we never fight the panel's post-crossing reset.
            bool writeAttrs = !crossed && attrsDirty;
            bool writeParts = !crossed && partsDirty;
            bool writeLabel = !crossed && labelDirty;

            if (!codeDirty && !writeAttrs && !writeParts && !writeLabel)
                return new PanelUserWriteResult(true) { UpdatedUser = user };

            if (string.IsNullOrWhiteSpace(masterCode))
                return new PanelUserWriteResult(false, "Master code is required");

            // All the panel commands below run inside the access code service's scope:
            // shared lock acquired, panel in AccessCodeProgramming mode, automatic exit on all paths.
            var scoped = await _accessCodes.ExecuteAsync(
                sessionId, PanelAccessCodeKind.Master, masterCode, readWrite: true,
                operation: async innerCt =>
                {
                    var errors = new List<string>();
                    int idx = user.UserIndex;

                    if (codeDirty)
                    {
                        if (!await SendCommandAsync(sessionId, new AccessCodeWrite
                        {
                            AccessCodeStart = idx,
                            AccessCodeCount = 1,
                            AccessCodes = [user.CodeValue ?? ""]
                        }, innerCt))
                            errors.Add("Access code");
                    }

                    if (writeAttrs)
                    {
                        if (!await SendCommandAsync(sessionId, new AccessCodeAttributeWrite
                        {
                            AccessCodeStart = idx,
                            AccessCodeCount = 1,
                            DataWidth = 1,
                            Attributes = [user.Attributes]
                        }, innerCt))
                            errors.Add("Attributes");
                    }

                    if (writeParts)
                    {
                        int maxPartitions = _panelState.GetSession(sessionId)?.MaxPartitions ?? 8;
                        int dataWidth = Math.Max(1, (maxPartitions + 7) / 8);
                        var bitmask = new byte[dataWidth];
                        foreach (var p in user.Partitions)
                        {
                            if (p < 1 || p > maxPartitions)
                            {
                                _logger.LogWarning("User {Index}: partition {Partition} out of range 1..{Max}, ignoring",
                                    idx, p, maxPartitions);
                                continue;
                            }
                            int byteIndex = (p - 1) / 8;
                            int bitIndex = (p - 1) % 8;
                            bitmask[byteIndex] |= (byte)(1 << bitIndex);
                        }

                        if (!await SendCommandAsync(sessionId, new AccessCodePartitionAssignmentWrite
                        {
                            AccessCodeStart = idx,
                            AccessCodeCount = 1,
                            DataWidth = (byte)dataWidth,
                            PartitionBitmask = bitmask
                        }, innerCt))
                            errors.Add("Partition assignments");
                    }

                    if (writeLabel)
                    {
                        if (!await SendCommandAsync(sessionId, new AccessCodeLabelWrite
                        {
                            AccessCodeStart = idx,
                            AccessCodeCount = 1,
                            AccessCodeLabels = [user.UserLabel ?? ""]
                        }, innerCt))
                            errors.Add("Label");
                    }

                    if (errors.Count > 0)
                        return new PanelUserWriteResult(false, $"Failed to write: {string.Join(", ", errors)}");

                    // On a crossing, re-read the authoritative panel state so our local copy
                    // reflects whatever the panel actually did (attrs/partitions reset, etc.).
                    if (crossed)
                        await RefreshSingleUserAsync(sessionId, user, innerCt);

                    var session = _panelState.GetSession(sessionId);
                    if (session != null)
                    {
                        user.LastUpdated = DateTime.UtcNow;
                        session.Users[idx] = user;
                        _panelState.UpdateSession(sessionId, _ => { });
                    }

                    return new PanelUserWriteResult(true)
                    {
                        Crossed = crossed,
                        UpdatedUser = user,
                    };
                },
                ct);

            if (!scoped.Success)
                return new PanelUserWriteResult(false, scoped.ErrorMessage ?? "Failed to write user");

            return scoped.Result ?? new PanelUserWriteResult(false, "Write returned no result");
        }

        public Task<PanelUserWriteResult> DisableUserAsync(string sessionId, int userIndex, string masterCode, CancellationToken ct)
        {
            var session = _panelState.GetSession(sessionId);
            if (session is null || !session.Users.TryGetValue(userIndex, out var existing))
                return Task.FromResult(new PanelUserWriteResult(false, "User not found in session"));

            // Sentinel length must match the panel's configured code length so the panel round-trips it.
            int codeLength = existing.CodeLength is 4 or 6 or 8 ? existing.CodeLength!.Value : 4;
            var sentinel = Models.PanelUserState.DisabledAccessCode(codeLength);

            var edited = CloneForEdit(existing);
            edited.CodeValue = sentinel;
            edited.CodeLength = codeLength;

            // WriteUserAsync detects the crossing, writes the code only, re-reads the slot.
            return WriteUserAsync(sessionId, edited, existing, masterCode, ct);
        }

        public Task<PanelUserWriteResult> EnableUserAsync(string sessionId, int userIndex, string newCode, string masterCode, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(newCode)
                || (newCode.Length != 4 && newCode.Length != 6 && newCode.Length != 8)
                || newCode.Any(c => c < '0' || c > '9'))
            {
                return Task.FromResult(new PanelUserWriteResult(false, "Access code must be 4, 6, or 8 digits"));
            }

            var session = _panelState.GetSession(sessionId);
            if (session is null || !session.Users.TryGetValue(userIndex, out var existing))
                return Task.FromResult(new PanelUserWriteResult(false, "User not found in session"));

            var edited = CloneForEdit(existing);
            edited.CodeValue = newCode;
            edited.CodeLength = newCode.Length;

            return WriteUserAsync(sessionId, edited, existing, masterCode, ct);
        }

        /// <summary>
        /// Shallow clone of a <see cref="Models.PanelUserState"/> for use as the "edited" input
        /// to <see cref="WriteUserAsync"/>. Uses a fresh <c>Partitions</c> list so dirty-check
        /// sees an independent sequence even though the values are equal.
        /// </summary>
        private static Models.PanelUserState CloneForEdit(Models.PanelUserState src) => new()
        {
            UserIndex = src.UserIndex,
            UserLabel = src.UserLabel,
            CodeValue = src.CodeValue,
            CodeLength = src.CodeLength,
            Attributes = src.Attributes,
            Partitions = new List<byte>(src.Partitions),
            HasProximityTag = src.HasProximityTag,
        };

        /// <summary>
        /// Re-reads attributes, partitions, and code configuration for a single user slot
        /// and patches the in-place <paramref name="user"/> with the fresh values.
        /// Assumes the caller is already in AccessCodeProgramming mode. Access code and
        /// label are not re-read; the access code was just written and the label is
        /// unaffected by enable/disable crossings.
        /// </summary>
        private async Task RefreshSingleUserAsync(string sessionId, Models.PanelUserState user, CancellationToken ct)
        {
            int idx = user.UserIndex;

            var attrResp = await SendRequestAsync<AccessCodeAttributeReadResponse>(
                sessionId,
                new AccessCodeAttributeReadRequest { AccessCodeStart = idx, AccessCodeCount = 1 },
                ct);
            if (attrResp?.Attributes is { Length: > 0 } attrs)
                user.Attributes = attrs[0];

            var partResp = await SendRequestAsync<AccessCodePartitionAssignmentReadResponse>(
                sessionId,
                new AccessCodePartitionAssignmentReadRequest { AccessCodeStart = idx, AccessCodeCount = 1 },
                ct);
            if (partResp?.PartitionAssignments is { Length: > 0 } parts)
                user.Partitions = parts[0];

            var confResp = await SendRequestAsync<UserCodeConfigurationReadResponse>(
                sessionId,
                new UserCodeConfigurationReadRequest { UserCodeStart = idx, UserCodeCount = 1 },
                ct);
            if (confResp?.CodeType is { Length: > 0 } codeTypes)
                user.HasProximityTag = codeTypes[0] == UserCodeConfigurationReadResponse.UserCodeType.ProximityTag;
        }

        private async Task<bool> SendCommandAsync(string sessionId, IMessageData command, CancellationToken ct)
        {
            var response = await _mediator.Send(new SessionCommand
            {
                SessionID = sessionId,
                MessageData = command
            }, ct);

            if (!response.Success)
                _logger.LogWarning("Write command {Command} failed: {Error}", command.GetType().Name, response.ErrorMessage);

            return response.Success;
        }

        // ── Progress reporting ────────────────────────────────────────────

        private void UpdateProgress(string sessionId, string message)
        {
            _panelState.UpdateSession(sessionId, s => s.UserReadProgress = message);
        }

        // ── User labels (via NotificationLabelText) ─────────────────────

        /// <summary>
        /// Reads user labels from the panel. Called before entering programming mode.
        /// Best-effort — returns empty dictionary on failure.
        /// </summary>
        private async Task<Dictionary<int, string>> ReadUserLabelsAsync(
            string sessionId, int maxUsers, CancellationToken ct)
        {
            var labels = new Dictionary<int, string>();

            try
            {
                for (int start = 1; start <= maxUsers; start += MaxLabelsPerRequest)
                {
                    int end = Math.Min(start + MaxLabelsPerRequest - 1, maxUsers);

                    var response = await SendRequestAsync<NotificationLabelText>(
                        sessionId,
                        new CommandRequestMessage
                        {
                            Request = new NotificationLabelText
                            {
                                Collection = NotificationLabelText.LabelCollection.User,
                                Start = start,
                                End = end
                            }
                        },
                        ct);

                    if (response == null)
                    {
                        _logger.LogDebug(
                            "User label request failed for range {Start}-{End} (type {Type}), stopping label read",
                            start, end, NotificationLabelText.LabelCollection.User);
                        break;
                    }

                    for (int i = 0; i < response.Labels.Length; i++)
                    {
                        int userIndex = start + i;
                        var label = response.Labels[i]?.Trim();
                        if (!string.IsNullOrEmpty(label))
                            labels[userIndex] = label;
                    }
                }

                if (labels.Count > 0)
                    _logger.LogInformation("Read {Count} user labels (type {Type})", labels.Count, NotificationLabelText.LabelCollection.User);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "User label reading failed (type {Type})", NotificationLabelText.LabelCollection.User);
            }

            return labels;
        }

        // ── Batch size calculation ────────────────────────────────────────

        /// <summary>
        /// Calculates how many records fit in a single response message.
        /// </summary>
        private static int MaxRecordsPerBatch(int bytesPerRecord, int headerOverhead = BatchHeaderOverhead)
            => Math.Max(1, (MaxPayloadBytes - headerOverhead) / Math.Max(1, bytesPerRecord));

        // ── Batched user reading ─────────────────────────────────────────

        private async Task<PanelUserReadResult> ReadAllUsersAsync(
            string sessionId, Models.SessionState session, Dictionary<int, string> labels, CancellationToken ct)
        {
            int maxUsers = session.MaxUsers;
            int partitionWidth = Math.Max(1, (session.MaxPartitions + 7) / 8);
            var sw = Stopwatch.StartNew();

            UpdateProgress(sessionId, "Reading access codes…");
            // Conservative: 8-digit codes = 4 BCD bytes
            var codes = await ReadBatchedAsync<AccessCodeReadResponse, string>(
                sessionId, maxUsers,
                MaxRecordsPerBatch(bytesPerRecord: 4),
                (start, count) => new AccessCodeReadRequest { AccessCodeStart = start, AccessCodeCount = count },
                r => r.AccessCodes,
                ct);

            UpdateProgress(sessionId, "Reading attributes…");
            var attrs = await ReadBatchedAsync<AccessCodeAttributeReadResponse, PanelUserAttributes>(
                sessionId, maxUsers,
                MaxRecordsPerBatch(bytesPerRecord: 1),
                (start, count) => new AccessCodeAttributeReadRequest { AccessCodeStart = start, AccessCodeCount = count },
                r => r.Attributes,
                ct);

            UpdateProgress(sessionId, "Reading partition assignments…");
            var parts = await ReadBatchedAsync<AccessCodePartitionAssignmentReadResponse, List<byte>>(
                sessionId, maxUsers,
                MaxRecordsPerBatch(bytesPerRecord: partitionWidth),
                (start, count) => new AccessCodePartitionAssignmentReadRequest { AccessCodeStart = start, AccessCodeCount = count },
                r => r.PartitionAssignments,
                ct);

            UpdateProgress(sessionId, "Reading code configuration…");
            var confs = await ReadBatchedAsync<UserCodeConfigurationReadResponse, UserCodeConfigurationReadResponse.UserCodeType>(
                sessionId, maxUsers,
                MaxRecordsPerBatch(bytesPerRecord: 1),
                (start, count) => new UserCodeConfigurationReadRequest { UserCodeStart = start, UserCodeCount = count },
                r => r.CodeType,
                ct);

            // Assemble PanelUserState from batch results
            int readCount = 0, failedCount = 0;
            for (int i = 1; i <= maxUsers; i++)
            {
                var state = new Models.PanelUserState { UserIndex = i };

                if (labels.TryGetValue(i, out var label))
                    state.UserLabel = label;

                if (codes.TryGetValue(i, out var code))
                {
                    state.CodeValue = code;
                    state.CodeLength = code?.Length;
                    readCount++;
                }
                else
                {
                    failedCount++;
                }

                if (attrs.TryGetValue(i, out var attr))
                {
                    state.Attributes = attr;
                }

                if (parts.TryGetValue(i, out var partList))
                    state.Partitions = partList;

                if (confs.TryGetValue(i, out var codeType))
                    state.HasProximityTag = codeType == UserCodeConfigurationReadResponse.UserCodeType.ProximityTag;

                state.LastUpdated = DateTime.UtcNow;
                session.Users[i] = state;
            }

            sw.Stop();
            session.UserReadCurrent = maxUsers;
            session.UserReadProgress = "Finishing…";
            _panelState.UpdateSession(sessionId, _ => { });

            _logger.LogInformation(
                "Read {Total} users in {Elapsed}ms ({Ok} OK, {Failed} failed)",
                maxUsers, sw.ElapsedMilliseconds, readCount, failedCount);

            session.UsersLastReadAt = DateTime.UtcNow;
            _panelState.UpdateSession(sessionId, _ => { });

            return new PanelUserReadResult(true)
            {
                ReadCount = readCount,
                FailedCount = failedCount,
            };
        }

        /// <summary>
        /// Reads a data type for all users in batches, returning a dictionary keyed by 1-based user index.
        /// </summary>
        private async Task<Dictionary<int, T>> ReadBatchedAsync<TResp, T>(
            string sessionId,
            int totalUsers,
            int batchSize,
            Func<int, int, IMessageData> createRequest,
            Func<TResp, T[]> extractItems,
            CancellationToken ct)
            where TResp : class, IMessageData
        {
            var result = new Dictionary<int, T>(totalUsers);

            for (int start = 1; start <= totalUsers; start += batchSize)
            {
                int count = Math.Min(batchSize, totalUsers - start + 1);
                var response = await SendRequestAsync<TResp>(sessionId, createRequest(start, count), ct);
                if (response == null)
                {
                    _logger.LogWarning("Batch {Type} failed for range {Start}–{End}",
                        typeof(TResp).Name, start, start + count - 1);
                    continue;
                }

                var items = extractItems(response);
                for (int i = 0; i < items.Length && start + i <= totalUsers; i++)
                    result[start + i] = items[i];
            }

            return result;
        }

        // ── Programming mode management ──────────────────────────────────

        /// <summary>
        /// Generic send helper for commands that return a bool success (no response body needed).
        /// </summary>
        private async Task<bool> SendCommandAsync(string sessionId, IMessageData command, CancellationToken ct)
        {
            var session = _panelState.GetSession(sessionId);
            if (session == null)
                return false;

            // If panel is already in programming mode, exit first to avoid "partition busy"
            if (session.IsInProgrammingMode)
            {
                _logger.LogDebug("Panel already in programming mode, exiting first");
                await ExitConfigModeAsync(sessionId, ct);
                await Task.Delay(500, ct);
            }

            _logger.LogInformation("Entering {Mode} mode", mode);

            var enterResponse = await _mediator.Send(new SessionCommand
            {
                SessionID = sessionId,
                MessageData = new ConfigurationEnter
                {
                    Partition = 1,
                    ProgrammingMode = mode,
                    AccessCode = accessCode,
                    ReadWrite = readWrite ? ConfigurationEnter.ReadWriteAccessEnum.ReadWriteMode : ConfigurationEnter.ReadWriteAccessEnum.ReadOnlyMode
                }
            }, ct);

            if (!enterResponse.Success)
            {
                _logger.LogWarning("Failed to enter {Mode} mode: {Error}", mode, enterResponse.ErrorMessage);
                return false;
            }

            _logger.LogDebug("ConfigurationEnter accepted, waiting for LeadIn...");

            // Wait for the panel to confirm programming mode.
            var deadline = DateTime.UtcNow + ProgrammingModeTimeout;
            while (!session.IsInProgrammingMode && DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(100, ct);
            }

            if (!session.IsInProgrammingMode)
            {
                _logger.LogWarning("Timed out waiting for LeadIn after {Timeout}s", ProgrammingModeTimeout.TotalSeconds);
                // Try to exit cleanly even though we timed out
                await ExitConfigModeAsync(sessionId, ct);
                return false;
            }

            _logger.LogInformation("Panel confirmed {Mode} mode", mode);
            return true;
        }

        /// <summary>
        /// Exits configuration mode. Best-effort — logs but does not throw on failure.
        /// </summary>
        private async Task ExitConfigModeAsync(string sessionId, CancellationToken ct)
        {
            try
            {
                _logger.LogDebug("Sending ConfigurationExit");
                await _mediator.Send(new SessionCommand
                {
                    SessionID = sessionId,
                    MessageData = new ConfigurationExit { Partition = 1 }
                }, ct);
                _logger.LogDebug("Exited configuration mode");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to exit configuration mode");
            }
        }

        private async Task<T?> SendRequestAsync<T>(string sessionId, IMessageData request, CancellationToken ct)
            where T : class, IMessageData
        {
            _logger.LogDebug("Sending {Command}", request.GetType().Name);

            var response = await _mediator.Send(new SessionCommand
            {
                SessionID = sessionId,
                MessageData = request
            }, ct);

            if (!response.Success)
            {
                _logger.LogWarning("User read command {Command} failed: {Error}", request.GetType().Name, response.ErrorMessage);
                return null;
            }

            return response.MessageData as T;
        }
    }
}
