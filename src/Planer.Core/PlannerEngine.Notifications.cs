namespace Planer.Core;

public enum PlannerNotificationKind { Morning, Rollover, HabitMilestone }
public enum PlannerDestination { Week, Today, Habits }

public sealed class PlannerNotification
{
    public string Key { get; set; } = "";
    public PlannerNotificationKind Kind { get; set; }
    public PlannerDestination Destination { get; set; }
    public string Body { get; set; } = "";
    public DateOnly Created { get; set; }
    public bool Dispatched { get; set; }
    public Guid? HabitId { get; set; }
    public DateOnly? StreakStart { get; set; }
    public int Milestone { get; set; }
}

public sealed partial class PlannerEngine
{
    public const string MorningMessage = "Доброе утро! Посмотри, чем мы сегодня хотим заняться ;)";
    public const string RolloverMessage = "Ой, кажется, мы что-то забыли! :( Давай вернем задачи на место";
    private static readonly int[] HabitMilestones = [14, 30, 60, 90];

    private void InitializeNotificationTracking()
    {
        if (State.NotificationTrackingInitialized) return;
        Commit(() =>
        {
            // Existing databases start quietly; previously reached milestones are a baseline.
            foreach (var habit in State.Habits)
            {
                var streak = HabitStreak(habit.Id);
                foreach (int milestone in HabitMilestones.Where(m => m <= streak.Days))
                    State.Notifications.Add(HabitNotification(habit, streak, milestone, dispatched: true));
            }
            State.NotificationTrackingInitialized = true;
        }, notify: false);
    }

    public void SetNotificationsEnabled(bool enabled) => Commit(() => State.NotificationsEnabled = enabled, notify: false);

    private void QueueNotification(PlannerNotification notification)
    {
        if (State.NotificationsEnabled && State.Notifications.All(n => n.Key != notification.Key)) State.Notifications.Add(notification);
    }
    private PlannerNotification HabitNotification(Habit habit, HabitStreakInfo streak, int milestone, bool dispatched = false) => new()
    {
        Key = $"habit:{habit.Id}:{streak.Start:yyyy-MM-dd}:{milestone}", Kind = PlannerNotificationKind.HabitMilestone,
        Destination = PlannerDestination.Habits, Created = Today, HabitId = habit.Id, StreakStart = streak.Start, Milestone = milestone,
        Body = $"Привычка «{habit.Name}» выполняется уже {milestone} дней подряд! Ты молодец <3", Dispatched = dispatched
    };
    private void QueueHabitMilestone(Habit habit)
    {
        if (habit.Ended is not null || !State.NotificationsEnabled) return;
        var streak = HabitStreak(habit.Id);
        int milestone = HabitMilestones.LastOrDefault(m => m <= streak.Days);
        if (milestone > 0) QueueNotification(HabitNotification(habit, streak, milestone));
    }

    public void CheckNotificationDue(DateTime localNow)
    {
        if (!State.NotificationsEnabled || DateOnly.FromDateTime(localNow) != Today) return;
        var before = State.Notifications.Select(n => n.Key).ToHashSet();
        try
        {
            if (localNow.TimeOfDay >= TimeSpan.FromHours(10) && localNow.TimeOfDay < TimeSpan.FromHours(12)
                && TasksOn(Today).Any())
                QueueNotification(new() { Key = $"morning:{Today:yyyy-MM-dd}", Kind = PlannerNotificationKind.Morning,
                    Destination = PlannerDestination.Today, Created = Today, Body = MorningMessage });
            foreach (var habit in State.Habits) QueueHabitMilestone(habit);
            if (State.Notifications.Any(n => !before.Contains(n.Key))) _store.Save(State);
        }
        catch { State = _store.Load(); throw; }
    }

    public IReadOnlyList<PlannerNotification> PendingNotifications(DateTime localNow) => State.Notifications.Where(n => !n.Dispatched
        && n.Created <= Today && IsRelevant(n, localNow)).ToList();
    private bool IsRelevant(PlannerNotification notification, DateTime localNow)
    {
        if (!State.NotificationsEnabled) return false;
        if (notification.Kind == PlannerNotificationKind.Morning)
            return notification.Created == Today && localNow.TimeOfDay >= TimeSpan.FromHours(10) && localNow.TimeOfDay < TimeSpan.FromHours(12)
                && TasksOn(Today).Any();
        if (Today.DayNumber - notification.Created.DayNumber > 7) return false;
        if (notification.HabitId is { } id)
        {
            var streak = HabitStreak(id);
            return streak.Days >= notification.Milestone && streak.Start == notification.StreakStart;
        }
        return true;
    }
    public void AcknowledgeNotification(string key) => Commit(() => State.Notifications.First(n => n.Key == key).Dispatched = true, notify: false);
}
