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
        /// How long to wait for the panel to confirm programming mode entry (0x0702 LeadIn)
        /// after sending ConfigurationEnter (0x0704).
        /// </summary>
        private static readonly TimeSpan ProgrammingModeTimeout = TimeSpan.FromSeconds(10);

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

            // Access code commands (0x0736/37/38/3C) are sent as direct ITv2 commands in AccessCodeProgramming mode.
            var masterCode = _appSettings.CurrentValue.DefaultAccessCode;
            if (string.IsNullOrWhiteSpace(masterCode))
                return new AccessCodeReadResult(false, "Master code (DefaultAccessCode) not configured");

            if (!await EnterAccessCodeModeAsync(sessionId, masterCode, ct))
                return new AccessCodeReadResult(false, "Failed to enter programming mode");

            try
            {
                return await ReadAllUsersAsync(sessionId, session, ct);
            }
            finally
            {
                await ExitConfigModeAsync(sessionId, ct);
            }
        }

        private async Task<AccessCodeReadResult> ReadAllUsersAsync(
            string sessionId, Models.SessionState session, CancellationToken ct)
        {
            var readCount = 0;
            var failedCount = 0;

            for (int userIndex = 1; userIndex <= session.MaxUsers; userIndex++)
            {
                var state = session.AccessCodes.TryGetValue(userIndex, out var existing)
                    ? existing
                    : new Models.AccessCodeState { UserIndex = userIndex };

                bool ok = true;

                var accessCode = await SendRequestAsync<AccessCodeReadResponse>(
                    sessionId,
                    new AccessCodeReadRequest { UserNumberStart = userIndex, NumberOfUsers = 1 },
                    ct);
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

                var attr = await SendRequestAsync<AccessCodeAttributeReadResponse>(
                    sessionId,
                    new AccessCodeAttributeReadRequest { UserNumberStart = userIndex, NumberOfUsers = 1 },
                    ct);
                if (attr != null)
                {
                    state.RawAttributes = attr.Data;
                    state.IsActive = IsUserActive(attr.Data);
                }

                var partitions = await SendRequestAsync<AccessCodePartitionAssignmentReadResponse>(
                    sessionId,
                    new AccessCodePartitionAssignmentReadRequest { UserNumberStart = userIndex, NumberOfUsers = 1 },
                    ct);
                if (partitions != null)
                {
                    state.RawPartitionAssignments = partitions.Data;
                    state.Partitions = DecodePartitionAssignments(partitions.Data);
                }

                var config = await SendRequestAsync<UserCodeConfigurationReadResponse>(
                    sessionId,
                    new UserCodeConfigurationReadRequest { UserNumberStart = userIndex, NumberOfUsers = 1 },
                    ct);
                if (config != null)
                {
                    state.RawConfiguration = config.Data;
                    state.HasProximityTag = ParseHasProximityTag(config.Data);
                    state.Label = BuildLabel(userIndex, state);
                }

                state.LastUpdated = DateTime.UtcNow;
                session.AccessCodes[userIndex] = state;

                if (ok) readCount++;
                else failedCount++;
            }

            session.AccessCodesLastReadAt = DateTime.UtcNow;
            _panelState.UpdateSession(sessionId, _ => { });

            return new AccessCodeReadResult(true)
            {
                ReadCount = readCount,
                FailedCount = failedCount,
            };
        }

        /// <summary>
        /// Enters programming mode for access code operations by sending ConfigurationEnter (0x0704)
        /// with ProgrammingMode.AccessCodeProgramming and the panel's master code,
        /// then waits for the panel to confirm via the ProgrammingLeadInOut (0x0702) notification.
        /// Access code commands (0x0736/37/38/3C) require AccessCodeProgramming mode specifically —
        /// InstallersProgramming returns NotInCorrectProgrammingMode.
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

            _logger.LogInformation("Entering AccessCodeProgramming mode (0x0704, ProgrammingMode=AccessCodeProgramming, using master code)");

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

            _logger.LogDebug("ConfigurationEnter (0x0704) accepted, waiting for ProgrammingLeadIn (0x0702)...");

            // Wait for the panel to confirm programming mode via 0x0702 LeadIn notification.
            // The ProgrammingLeadInOutHandler sets session.IsInProgrammingMode = true.
            var deadline = DateTime.UtcNow + ProgrammingModeTimeout;
            while (!session.IsInProgrammingMode && DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(100, ct);
            }

            if (!session.IsInProgrammingMode)
            {
                _logger.LogWarning("Timed out waiting for ProgrammingLeadIn (0x0702) after {Timeout}s", ProgrammingModeTimeout.TotalSeconds);
                // Try to exit cleanly even though we timed out
                await ExitConfigModeAsync(sessionId, ct);
                return false;
            }

            _logger.LogInformation("Panel confirmed Access Code Programming mode (0x0702 LeadIn received)");
            return true;
        }

        /// <summary>
        /// Exits configuration mode by sending ConfigurationExit (0x0701).
        /// Always best-effort — logs but does not throw on failure.
        /// </summary>
        private async Task ExitConfigModeAsync(string sessionId, CancellationToken ct)
        {
            try
            {
                _logger.LogDebug("Sending ConfigurationExit (0x0701)");
                await _mediator.Send(new SessionCommand
                {
                    SessionID = sessionId,
                    MessageData = new ConfigurationExit { Partition = 1 }
                }, ct);
                _logger.LogDebug("Exited configuration mode");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to exit configuration mode (0x0701)");
            }
        }

        private async Task<T?> SendRequestAsync<T>(string sessionId, IMessageData request, CancellationToken ct)
            where T : class, IMessageData
        {
            // Requests extend CommandMessageBase — ITv2Session sends them directly and waits for response (cmd | 0x4000).
            _logger.LogDebug("Sending direct command {Command}", request.GetType().Name);

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
        // All four access-code response payloads (0x4736/37/38/3C) share a common
        // header structure when requesting a single user at a time:
        //
        //   [NumberOfRecords : 1 byte]   always 0x01
        //   [UserNumber      : 1 byte]
        //   [Unknown          : 1 byte]   always 0x01
        //   [Unknown          : 1 byte]   always 0x01
        //   [DataLength       : 1 byte]   length of the data portion that follows
        //   [Data             : DataLength bytes]
        //
        // The 5-byte header must be skipped before parsing the actual data.

        private const int ResponseHeaderSize = 5;

        /// <summary>
        /// Extracts a BCD-encoded PIN code from an AccessCodeRead (0x4736) response payload.
        /// Each data byte encodes two decimal digits (high nibble, low nibble).
        /// A byte value of 0xAA indicates an empty/unset code slot.
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

                // 0xAA = empty/unset sentinel (displays as "AAAA" in DLS5)
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
        /// Checks if a user slot is active based on AccessCodeAttributeRead (0x4737) response.
        /// A non-zero attribute byte indicates an active/configured user.
        /// Known values: 0x0C = active with options, 0x00 = empty/disabled.
        /// </summary>
        private static bool IsUserActive(byte[] payload)
        {
            if (payload == null || payload.Length < ResponseHeaderSize + 1)
                return false;

            int dataLen = payload[4];
            if (dataLen == 0 || payload.Length < ResponseHeaderSize + dataLen)
                return false;

            return payload[ResponseHeaderSize] != 0x00;
        }

        /// <summary>
        /// Decodes partition assignments from an AccessCodePartitionAssignmentRead (0x4738) response.
        /// The data byte(s) after the header are a bitmask: bit N (LSB-first) = partition N+1.
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
        /// Checks if the user has a proximity tag (keyfob) from UserCodeConfigurationRead (0x473C).
        /// Known values: 0x00 = none, 0x01 = standard access code (PIN only), 0x02 = proximity tag.
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

        /// <summary>
        /// Builds a human-readable label for the user based on their configuration.
        /// Mirrors the DLS5 display where user 1 is "Master" and others show their type.
        /// </summary>
        private static string BuildLabel(int userIndex, Models.AccessCodeState state)
        {
            if (userIndex == 1)
                return "Master";

            var parts = new List<string>();

            if (state.CodeValue != null)
                parts.Add("PIN");

            if (state.HasProximityTag)
                parts.Add("Proximity Tag");

            if (parts.Count > 0)
                return string.Join(" + ", parts);

            return state.IsActive ? "Active" : "";
        }
    }
}
