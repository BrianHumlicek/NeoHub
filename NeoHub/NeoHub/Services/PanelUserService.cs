using System.Diagnostics;
using DSC.TLink.ITv2;
using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.ITv2.MediatR;
using DSC.TLink.ITv2.Messages;
using MediatR;
using Microsoft.Extensions.Options;
using NeoHub.Services.Settings;
using static DSC.TLink.ITv2.Messages.AccessCodeAttributeReadResponse;
using static DSC.TLink.ITv2.Messages.UserCodeConfigurationReadResponse;

namespace NeoHub.Services
{
    public class PanelUserService : IPanelUserService
    {
        private readonly IMediator _mediator;
        private readonly IPanelStateService _panelState;
        private readonly IOptionsMonitor<ApplicationSettings> _appSettings;
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
            IOptionsMonitor<ApplicationSettings> appSettings,
            ILogger<PanelUserService> logger)
        {
            _mediator = mediator;
            _panelState = panelState;
            _appSettings = appSettings;
            _logger = logger;
        }

        public async Task<PanelUserReadResult> ReadAllAsync(string sessionId, CancellationToken ct)
        {
            var session = _panelState.GetSession(sessionId);
            if (session == null)
                return new PanelUserReadResult(false, "Session not found");

            if (session.MaxUsers <= 0)
                return new PanelUserReadResult(false, "Panel reported zero users");

            _logger.LogInformation("Reading panel users for session {SessionId}, max users: {MaxUsers}", sessionId, session.MaxUsers);

            // Access code commands require AccessCodeProgramming mode with the master code.
            var masterCode = _appSettings.CurrentValue.DefaultAccessCode;
            if (string.IsNullOrWhiteSpace(masterCode))
                return new PanelUserReadResult(false, "Master code (DefaultAccessCode) not configured");

            session.IsReadingUsers = true;
            session.UserReadCurrent = 0;
            session.UserReadTotal = session.MaxUsers;
            session.UserReadProgress = null;
            _panelState.UpdateSession(sessionId, _ => { });

            try
            {
                // 1. Read user labels first (no programming mode needed, uses CommandRequestMessage)
                UpdateProgress(session, sessionId, "Reading user labels…");
                var labels = await ReadUserLabelsAsync(sessionId, session.MaxUsers, ct);

                // 2. Enter programming mode for access code commands
                UpdateProgress(session, sessionId, "Entering programming mode…");
                if (!await EnterAccessCodeModeAsync(sessionId, masterCode, ct))
                    return new PanelUserReadResult(false, "Failed to enter programming mode");

                PanelUserReadResult result;
                try
                {
                    // 3. Batch-read all user data types in parallel
                    result = await ReadAllUsersAsync(sessionId, session, labels, ct);
                }
                finally
                {
                    await ExitConfigModeAsync(sessionId, ct);
                }

                return result;
            }
            finally
            {
                session.IsReadingUsers = false;
                session.UserReadProgress = null;
                session.UserReadCurrent = 0;
                session.UserReadTotal = 0;
                _panelState.UpdateSession(sessionId, _ => { });
            }
        }

        // ── Progress reporting ────────────────────────────────────────────

        private void UpdateProgress(Models.SessionState session, string sessionId, string message)
        {
            session.UserReadProgress = message;
            _panelState.UpdateSession(sessionId, _ => { });
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

            UpdateProgress(session, sessionId, "Reading user data…");

            // Run all 4 data type reads in parallel — each batches internally
            var codesTask = ReadBatchedAsync<AccessCodeReadResponse, string>(
                sessionId, maxUsers,
                MaxRecordsPerBatch(bytesPerRecord: 4), // Conservative: 8-digit codes = 4 BCD bytes
                (start, count) => new AccessCodeReadRequest { AccessCodeStart = start, AccessCodeCount = count },
                r => r.AccessCodes,
                ct);

            var attrsTask = ReadBatchedAsync<AccessCodeAttributeReadResponse, PanelUserAttributes>(
                sessionId, maxUsers,
                MaxRecordsPerBatch(bytesPerRecord: 1),
                (start, count) => new AccessCodeAttributeReadRequest { AccessCodeStart = start, AccessCodeCount = count },
                r => r.Attributes,
                ct);

            var partsTask = ReadBatchedAsync<AccessCodePartitionAssignmentReadResponse, List<byte>>(
                sessionId, maxUsers,
                MaxRecordsPerBatch(bytesPerRecord: partitionWidth),
                (start, count) => new AccessCodePartitionAssignmentReadRequest { AccessCodeStart = start, AccessCodeCount = count },
                r => r.PartitionAssignments,
                ct);

            var confsTask = ReadBatchedAsync<UserCodeConfigurationReadResponse, UserCodeType>(
                sessionId, maxUsers,
                MaxRecordsPerBatch(bytesPerRecord: 1),
                (start, count) => new UserCodeConfigurationReadRequest { UserCodeStart = start, UserCodeCount = count },
                r => r.CodeType,
                ct);

            await Task.WhenAll(codesTask, attrsTask, partsTask, confsTask);

            var codes = codesTask.Result;
            var attrs = attrsTask.Result;
            var parts = partsTask.Result;
            var confs = confsTask.Result;

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
                    state.IsActive = attr != PanelUserAttributes.None;
                }

                if (parts.TryGetValue(i, out var partList))
                    state.Partitions = partList;

                if (confs.TryGetValue(i, out var codeType))
                    state.HasProximityTag = codeType == UserCodeType.ProximityTag;

                state.LastUpdated = DateTime.UtcNow;
                session.Users[i] = state;
            }

            sw.Stop();
            session.UserReadCurrent = maxUsers;
            session.UserReadProgress = "Finishing…";
            _panelState.UpdateSession(sessionId, _ => { });

            _logger.LogInformation(
                "Read {Total} users in {Elapsed}ms ({Ok} OK, {Failed} failed, batches: codes={CodeBatches} attrs={AttrBatches} parts={PartBatches} conf={ConfBatches})",
                maxUsers, sw.ElapsedMilliseconds, readCount, failedCount,
                CeilDiv(maxUsers, MaxRecordsPerBatch(4)),
                CeilDiv(maxUsers, MaxRecordsPerBatch(1)),
                CeilDiv(maxUsers, MaxRecordsPerBatch(partitionWidth)),
                CeilDiv(maxUsers, MaxRecordsPerBatch(1)));

            session.UsersLastReadAt = DateTime.UtcNow;
            _panelState.UpdateSession(sessionId, _ => { });

            return new PanelUserReadResult(true)
            {
                ReadCount = readCount,
                FailedCount = failedCount,
            };
        }

        private static int CeilDiv(int a, int b) => (a + b - 1) / b;

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
        /// Enters AccessCodeProgramming mode using the master code,
        /// then waits for the panel to confirm via LeadIn notification.
        /// </summary>
        private async Task<bool> EnterAccessCodeModeAsync(string sessionId, string masterCode, CancellationToken ct)
        {
            var session = _panelState.GetSession(sessionId);
            if (session == null)
                return false;

            // If panel is already in programming mode, exit first to avoid "partition busy"
            if (session.IsInProgrammingMode)
            {
                _logger.LogDebug("Panel already in programming mode, exiting first before user read");
                await ExitConfigModeAsync(sessionId, ct);
                // Brief pause to let panel process the exit
                await Task.Delay(500, ct);
            }

            _logger.LogInformation("Entering AccessCodeProgramming mode");

            var enterResponse = await _mediator.Send(new SessionCommand
            {
                SessionID = sessionId,
                MessageData = new ConfigurationEnter
                {
                    Partition = 1,
                    ProgrammingMode = ProgrammingMode.AccessCodeProgramming,
                    AccessCode = masterCode,
                    ReadWrite = ConfigurationEnter.ReadWriteAccessEnum.ReadOnlyMode
                }
            }, ct);

            if (!enterResponse.Success)
            {
                _logger.LogWarning("Failed to enter Access Code Programming mode: {Error}", enterResponse.ErrorMessage);
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

            _logger.LogInformation("Panel confirmed AccessCodeProgramming mode");
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
