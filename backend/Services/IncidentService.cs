using backend.Data;
using backend.Models;
using backend.Models.Enums;
using backend.Services.Telegram;

namespace backend.Services;

public partial class IncidentService
{
    private static readonly TimeSpan IntrusionConfirmationWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PanicDedupWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SensorAnomalyWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MotionConfirmedCooldown = TimeSpan.FromHours(2);
    private static readonly TimeSpan MotionAnomalyDuringCooldownWindow = TimeSpan.FromMinutes(5);
    private const int MotionAnomalyDuringCooldownThreshold = 5;

    private readonly AppDbContext _db;
    private readonly ILogger<IncidentService> _logger;

    private readonly ITelegramNotificationService _telegram;

    public IncidentService(
        AppDbContext db,
        ILogger<IncidentService> logger,
        ITelegramNotificationService telegram)
    {
        _db = db;
        _logger = logger;
        _telegram = telegram;
    }

    public async Task ProcessEventAsync(SecurityEvent securityEvent, CancellationToken cancellationToken = default)
    {
        if (IsPanicTrigger(securityEvent))
        {
            await TryCreatePanicIncidentAsync(securityEvent, cancellationToken);
            return;
        }

        if (IsPerimeterTrigger(securityEvent))
        {
            await TryCreateOrConfirmIntrusionFromPerimeterAsync(securityEvent, cancellationToken);
        }

        if (IsMotionTrigger(securityEvent))
        {
            await TryCreateOrConfirmIntrusionFromMotionAsync(securityEvent, cancellationToken);
        }

        if (IsTamperTrigger(securityEvent))
        {
            await TryCreateOrConfirmSabotageFromTamperAsync(securityEvent, cancellationToken);
        }

        if (IsConnectionLostTrigger(securityEvent))
        {
            await TryCreateOrConfirmSabotageFromConnectionLostAsync(securityEvent, cancellationToken);
        }

        if (IsAnomalyCandidate(securityEvent))
        {
            await TryCreateSensorAnomalyAsync(securityEvent, cancellationToken);
        }
    }

    private static bool IsPanicTrigger(SecurityEvent e) =>
        e.Sensor == "panic_button" && e.Event == "pressed";

    private static bool IsPerimeterTrigger(SecurityEvent e) =>
        e.Armed && e.Sensor == "door" && e.Event == "open";

    private static bool IsMotionTrigger(SecurityEvent e) =>
        e.Armed && e.Sensor == "motion" && e.Event == "detected";

    private static bool IsTamperTrigger(SecurityEvent e) =>
        e.Armed && (e.Sensor == "door_tamper" || e.Sensor == "motion_tamper") && e.Event == "triggered";

    private static bool IsConnectionLostTrigger(SecurityEvent e) =>
        e.Armed && e.Sensor == "system" && e.Event == "connection_lost";

    private static bool IsAnomalyCandidate(SecurityEvent e) =>
        e.Sensor == "door" ||
        e.Sensor == "motion" ||
        e.Sensor == "door_tamper" ||
        e.Sensor == "motion_tamper";

    private static int SensorAnomalyThreshold(SecurityEvent e) =>
        e.Sensor == "motion" ? 5 : 10;
}
