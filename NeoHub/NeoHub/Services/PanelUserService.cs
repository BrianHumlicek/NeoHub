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
        /// Number of users to read concurrently. Each user sends 4 commands in parallel,
        /// so this value x 4 = max in-flight ITv2 commands. Keep at 1 to avoid
        /// flooding the panel's command buffer.
        /// </summary>
        private const int MaxConcurrentUserReads = 2;

        /// <summary>
        /// Max labels to request per batch via NotificationLabelText.
        /// </summary>
        private const int MaxLabelsPerRequest = 4;

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
                                LabelType = (int)LabelType.AccessCode,
                                Start = start,
                                End = end
                            }
                        },
                        ct);

                    if (response == null)
                    {
                        _logger.LogDebug(
                            "User label request failed for range {Start}-{End} (type {Type}), stopping label read",
                            start, end, LabelType.AccessCode);
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
                    _logger.LogInformation("Read {Count} user labels (type {Type})", labels.Count, LabelType.AccessCode);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "User label reading failed (type {Type})", LabelType.AccessCode);
            }

            return labels;
        }

        // ── Parallel user reading ────────────────────────────────────────

        private async Task<PanelUserReadResult> ReadAllUsersAsync(
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
                    session.UserReadCurrent = completed;
                    session.UserReadProgress = $"Reading user {userIndex}/{session.MaxUsers}…";
                    _panelState.UpdateSession(sessionId, _ => { });

                    var (state, ok) = await ReadSingleUserAsync(sessionId, userIndex, labels, token);
                    session.Users[userIndex] = state;

                    if (ok) Interlocked.Increment(ref readCount);
                    else Interlocked.Increment(ref failedCount);
                });

            sw.Stop();
            session.UserReadCurrent = session.MaxUsers;
            session.UserReadProgress = "Finishing…";
            _panelState.UpdateSession(sessionId, _ => { });

            _logger.LogInformation(
                "Read {Total} users in {Elapsed}ms ({Ok} OK, {Failed} failed, concurrency={Concurrency})",
                session.MaxUsers, sw.ElapsedMilliseconds, readCount, failedCount, MaxConcurrentUserReads);

            session.UsersLastReadAt = DateTime.UtcNow;
            _panelState.UpdateSession(sessionId, _ => { });

            return new PanelUserReadResult(true)
            {
                ReadCount = readCount,
                FailedCount = failedCount,
            };
        }

        /// <summary>
        /// Reads all 4 data points for a single user in parallel.
        /// Uses computed properties from formalized response messages.
        /// </summary>
        private async Task<(Models.PanelUserState state, bool ok)> ReadSingleUserAsync(
            string sessionId, int userIndex, Dictionary<int, string> labels, CancellationToken ct)
        {
            var state = new Models.PanelUserState { UserIndex = userIndex };

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
                state.CodeValue = accessCode.PinCode;
                state.CodeLength = state.CodeValue?.Length;
            }

            var attr = attrTask.Result;
            if (attr != null)
            {
                state.Attributes = (Models.PanelUserAttributes)attr.AttributeFlags;
                state.IsActive = state.Attributes != Models.PanelUserAttributes.None;
            }

            var partitions = partTask.Result;
            if (partitions != null)
            {
                state.Partitions = partitions.AssignedPartitions;
            }

            var config = confTask.Result;
            if (config != null)
            {
                state.HasProximityTag = config.HasProximityTag;
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
