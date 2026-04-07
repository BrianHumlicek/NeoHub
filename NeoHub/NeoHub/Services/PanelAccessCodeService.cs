using System.Diagnostics;
using DSC.TLink.ITv2;
using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.ITv2.MediatR;
using DSC.TLink.ITv2.Messages;
using MediatR;
using Microsoft.Extensions.Options;
using NeoHub.Services.Settings;

namespace NeoHub.Services
{
    public class PanelAccessCodeService : IPanelAccessCodeService
    {
        private readonly IMediator _mediator;
        private readonly IPanelStateService _panelState;
        private readonly IOptionsMonitor<ApplicationSettings> _appSettings;
        private readonly ILogger<PanelAccessCodeService> _logger;

        /// <summary>
        /// How long to wait for programming mode confirmation (LeadIn) after ConfigurationEnter.
        /// </summary>
        private static readonly TimeSpan ProgrammingModeTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Number of users to read concurrently. Each user sends 4 commands in parallel,
        /// so this value x 4 = max in-flight ITv2 commands. Keep at 1 to avoid
        /// flooding the panel's command buffer.
        /// </summary>
        private const int MaxConcurrentUserReads = 1;

        /// <summary>
        /// Max labels to request per batch via NotificationLabelText.
        /// </summary>
        private const int MaxLabelsPerRequest = 4;

        /// <summary>
        /// NotificationLabelText type for user/access code labels.
        /// Known types: 0xD1=zone, 0xD3=partition, 0xD9=user.
        /// </summary>
        private const int UserLabelTypeCode = 0xD9;

        public PanelAccessCodeService(
            IMediator mediator,
            IPanelStateService panelState,
            IOptionsMonitor<ApplicationSettings> appSettings,
            ILogger<PanelAccessCodeService> logger)
        {
            _mediator = mediator;
            _panelState = panelState;
            _appSettings = appSettings;
            _logger = logger;
        }

        public async Task<AccessCodeReadResult> ReadAllAsync(string sessionId, CancellationToken ct)
        {
            var session = _panelState.GetSession(sessionId);
            if (session == null)
                return new AccessCodeReadResult(false, "Session not found");

            if (session.MaxUsers <= 0)
                return new AccessCodeReadResult(false, "Panel reported zero users");

            _logger.LogInformation("Reading access codes for session {SessionId}, max users: {MaxUsers}", sessionId, session.MaxUsers);

            // Access code commands require AccessCodeProgramming mode with the master code.
            var masterCode = _appSettings.CurrentValue.DefaultAccessCode;
            if (string.IsNullOrWhiteSpace(masterCode))
                return new AccessCodeReadResult(false, "Master code (DefaultAccessCode) not configured");

            session.IsReadingAccessCodes = true;
            session.AccessCodeReadCurrent = 0;
            session.AccessCodeReadTotal = session.MaxUsers;
            session.AccessCodeReadProgress = null;
            _panelState.UpdateSession(sessionId, _ => { });

            try
            {
                // 1. Read user labels first (no programming mode needed, uses CommandRequestMessage)
                UpdateProgress(session, sessionId, "Reading user labels…");
                var labels = await ReadUserLabelsAsync(sessionId, session.MaxUsers, ct);

                // 2. Enter programming mode for access code commands
                UpdateProgress(session, sessionId, "Entering programming mode…");
                if (!await EnterAccessCodeModeAsync(sessionId, masterCode, ct))
                    return new AccessCodeReadResult(false, "Failed to enter programming mode");

                AccessCodeReadResult result;
                try
                {
                    // 3. Read all users (4 commands per user in parallel)
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
                session.IsReadingAccessCodes = false;
                session.AccessCodeReadProgress = null;
                session.AccessCodeReadCurrent = 0;
                session.AccessCodeReadTotal = 0;
                _panelState.UpdateSession(sessionId, _ => { });
            }
        }

        // ── Progress reporting ────────────────────────────────────────────

        private void UpdateProgress(Models.SessionState session, string sessionId, string message)
        {
            session.AccessCodeReadProgress = message;
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
                                Unknown = UserLabelTypeCode,
                                Start = start,
                                End = end
                            }
                        },
                        ct);

                    if (response == null)
                    {
                        _logger.LogDebug(
                            "User label request failed for range {Start}-{End} (type 0x{Type:X2}), stopping label read",
                            start, end, UserLabelTypeCode);
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
                    _logger.LogInformation("Read {Count} user labels (type 0x{Type:X2})", labels.Count, UserLabelTypeCode);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "User label reading failed (type 0x{Type:X2})", UserLabelTypeCode);
            }

            return labels;
        }

        // ── Parallel user reading ────────────────────────────────────────

        private async Task<AccessCodeReadResult> ReadAllUsersAsync(
            string sessionId, Models.SessionState session, Dictionary<int, string> labels, CancellationToken ct)
        {
            int readCount = 0;
            int failedCount = 0;
            var sw = Stopwatch.StartNew();

            await Parallel.ForEachAsync(
                Enumerable.Range(1, session.MaxUsers),
                new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentUserReads, CancellationToken = ct },
                async (userIndex, token) =>
                {
                    var completed = Interlocked.Add(ref readCount, 0) + Interlocked.Add(ref failedCount, 0);
                    session.AccessCodeReadCurrent = completed;
                    session.AccessCodeReadProgress = $"Reading user {userIndex}/{session.MaxUsers}…";
                    _panelState.UpdateSession(sessionId, _ => { });

                    var (state, ok) = await ReadSingleUserAsync(sessionId, userIndex, labels, token);
                    session.AccessCodes[userIndex] = state;

                    if (ok) Interlocked.Increment(ref readCount);
                    else Interlocked.Increment(ref failedCount);
                });

            sw.Stop();
            session.AccessCodeReadCurrent = session.MaxUsers;
            session.AccessCodeReadProgress = "Finishing…";
            _panelState.UpdateSession(sessionId, _ => { });

            _logger.LogInformation(
                "Read {Total} users in {Elapsed}ms ({Ok} OK, {Failed} failed, concurrency={Concurrency})",
                session.MaxUsers, sw.ElapsedMilliseconds, readCount, failedCount, MaxConcurrentUserReads);

            session.AccessCodesLastReadAt = DateTime.UtcNow;
            _panelState.UpdateSession(sessionId, _ => { });

            return new AccessCodeReadResult(true)
            {
                ReadCount = readCount,
                FailedCount = failedCount,
            };
        }

        /// <summary>
        /// Reads all 4 data points for a single user in parallel.
        /// </summary>
        private async Task<(Models.AccessCodeState state, bool ok)> ReadSingleUserAsync(
            string sessionId, int userIndex, Dictionary<int, string> labels, CancellationToken ct)
        {
            var state = new Models.AccessCodeState { UserIndex = userIndex };

            // Apply label if available
            if (labels.TryGetValue(userIndex, out var userLabel))
                state.UserLabel = userLabel;

            var codeTask = SendRequestAsync<AccessCodeReadResponse>(
                sessionId,
                new AccessCodeReadRequest { UserNumberStart = userIndex, NumberOfUsers = 1 },
                ct);
            var attrTask = SendRequestAsync<AccessCodeAttributeReadResponse>(
                sessionId,
                new AccessCodeAttributeReadRequest { UserNumberStart = userIndex, NumberOfUsers = 1 },
                ct);
            var partTask = SendRequestAsync<AccessCodePartitionAssignmentReadResponse>(
                sessionId,
                new AccessCodePartitionAssignmentReadRequest { UserNumberStart = userIndex, NumberOfUsers = 1 },
                ct);
            var confTask = SendRequestAsync<UserCodeConfigurationReadResponse>(
                sessionId,
                new UserCodeConfigurationReadRequest { UserNumberStart = userIndex, NumberOfUsers = 1 },
                ct);

            await Task.WhenAll(codeTask, attrTask, partTask, confTask);

            bool ok = true;

            var accessCode = codeTask.Result;
            if (accessCode == null)
            {
                ok = false;
            }
            else
            {
                state.RawAccessCode = accessCode.Data;
                state.CodeValue = TryExtractCodeDigits(accessCode.Data);
                state.CodeLength = state.CodeValue?.Length;
            }

            var attr = attrTask.Result;
            if (attr != null)
            {
                state.RawAttributes = attr.Data;
                state.Attributes = ParseAttributes(attr.Data);
                state.IsActive = state.Attributes != Models.AccessCodeAttributes.None;
            }

            var partitions = partTask.Result;
            if (partitions != null)
            {
                state.RawPartitionAssignments = partitions.Data;
                state.Partitions = DecodePartitionAssignments(partitions.Data);
            }

            var config = confTask.Result;
            if (config != null)
            {
                state.RawConfiguration = config.Data;
                state.HasProximityTag = ParseHasProximityTag(config.Data);
            }

            state.LastUpdated = DateTime.UtcNow;
            return (state, ok);
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
                _logger.LogDebug("Panel already in programming mode, exiting first before access code read");
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
                _logger.LogWarning("Access-code command {Command} failed: {Error}", request.GetType().Name, response.ErrorMessage);
                return null;
            }

            return response.MessageData as T;
        }

        // ── Response parsing ─────────────────────────────────────────────
        //
        // All four response payloads share a 5-byte header:
        //   [NumberOfRecords:1] [UserNumber:1] [Unknown:1] [Unknown:1] [DataLength:1] [Data...]

        private const int ResponseHeaderSize = 5;

        /// <summary>
        /// Extracts a BCD-encoded PIN from the response payload.
        /// Each byte = 2 digits (high/low nibble). 0xAA = empty slot.
        /// </summary>
        private static string? TryExtractCodeDigits(byte[] payload)
        {
            if (payload == null || payload.Length < ResponseHeaderSize + 1)
                return null;

            int dataLen = payload[4];
            if (dataLen == 0 || payload.Length < ResponseHeaderSize + dataLen)
                return null;

            var sb = new System.Text.StringBuilder(dataLen * 2);
            for (int i = ResponseHeaderSize; i < ResponseHeaderSize + dataLen; i++)
            {
                byte b = payload[i];

                // 0xAA = empty/unset sentinel
                if (b == 0xAA)
                    return null;

                int high = (b >> 4) & 0x0F;
                int low = b & 0x0F;

                // Valid BCD digits are 0-9
                if (high > 9 || low > 9)
                    return null;

                sb.Append((char)('0' + high));
                sb.Append((char)('0' + low));
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }

        /// <summary>
        /// Parses the attribute byte into individual bit flags.
        /// </summary>
        private static Models.AccessCodeAttributes ParseAttributes(byte[] payload)
        {
            if (payload == null || payload.Length < ResponseHeaderSize + 1)
                return Models.AccessCodeAttributes.None;

            int dataLen = payload[4];
            if (dataLen == 0 || payload.Length < ResponseHeaderSize + dataLen)
                return Models.AccessCodeAttributes.None;

            return (Models.AccessCodeAttributes)payload[ResponseHeaderSize];
        }

        /// <summary>
        /// Decodes partition assignments from a bitmask (LSB-first, bit N = partition N+1).
        /// </summary>
        private static List<byte> DecodePartitionAssignments(byte[] payload)
        {
            var result = new List<byte>();
            if (payload == null || payload.Length < ResponseHeaderSize + 1)
                return result;

            int dataLen = payload[4];
            if (dataLen == 0 || payload.Length < ResponseHeaderSize + dataLen)
                return result;

            for (int i = 0; i < dataLen; i++)
            {
                byte bitmap = payload[ResponseHeaderSize + i];
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((bitmap & (1 << bit)) != 0)
                    {
                        int partition = i * 8 + bit + 1;
                        if (partition <= byte.MaxValue)
                            result.Add((byte)partition);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Checks if the user has a proximity tag.
        /// Values: 0x00=none, 0x01=PIN, 0x02=proximity tag.
        /// </summary>
        private static bool ParseHasProximityTag(byte[] payload)
        {
            if (payload == null || payload.Length < ResponseHeaderSize + 1)
                return false;

            int dataLen = payload[4];
            if (dataLen == 0 || payload.Length < ResponseHeaderSize + dataLen)
                return false;

            return payload[ResponseHeaderSize] == 0x02;
        }
    }
}
