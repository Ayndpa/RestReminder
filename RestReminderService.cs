using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace RestReminder;

public sealed partial class RestReminderService : IDisposable
{
    private static readonly TimeSpan DefaultReminderInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan ReminderTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SnoozeInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PersistentNotificationRepeatInterval = TimeSpan.FromMinutes(1);
    private const string ReminderNotificationTag = "rest-reminder-alarm";
    private const string ReminderNotificationGroup = "rest-reminder";
    private const string StartBreakAction = "start-break";
    private const string ReturnAction = "return-from-break";
    private const string SnoozeAction = "snooze-reminder";

    private readonly Window _ownerWindow;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _hourlyTimer;
    private readonly DispatcherQueueTimer _persistentNotificationTimer;

    private ReminderWindow? _reminderWindow;
    private CancellationTokenSource? _timeoutCts;
    private DateTimeOffset? _persistentReminderTime;
    private bool _toastRegistered;

    public RestReminderService(Window ownerWindow)
    {
        _ownerWindow = ownerWindow;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread() ?? throw new InvalidOperationException("当前线程没有 DispatcherQueue。");
        _hourlyTimer = _dispatcherQueue.CreateTimer();
        _hourlyTimer.IsRepeating = true;
        _hourlyTimer.Tick += HourlyTimer_Tick;

        _persistentNotificationTimer = _dispatcherQueue.CreateTimer();
        _persistentNotificationTimer.IsRepeating = true;
        _persistentNotificationTimer.Interval = PersistentNotificationRepeatInterval;
        _persistentNotificationTimer.Tick += PersistentNotificationTimer_Tick;

        AppNotificationManager.Default.NotificationInvoked += AppNotificationManager_NotificationInvoked;
        AppNotificationManager.Default.Register();
        _toastRegistered = true;
    }

    public bool IsRunning { get; private set; }

    public TimeSpan ReminderInterval { get; private set; } = DefaultReminderInterval;

    public DateTimeOffset? NextReminderAt { get; private set; }

    public DateTimeOffset? CurrentReminderAt { get; private set; }

    public string? LastActionText { get; private set; }

    public event EventHandler? StateChanged;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        StartCountdown(DateTimeOffset.Now, "最后动作：已启动计时");
        RaiseStateChanged();
    }

    public void SetReminderInterval(TimeSpan interval)
    {
        if (interval < TimeSpan.FromMinutes(1))
        {
            interval = TimeSpan.FromMinutes(1);
        }

        ReminderInterval = interval;

        if (IsRunning && CurrentReminderAt is null)
        {
            StartCountdown(DateTimeOffset.Now, "最后动作：已更新提醒间隔并重新开始计时");
        }
        else
        {
            LastActionText = $"最后动作：已更新提醒间隔为 {ReminderInterval.TotalMinutes:0} 分钟";
            RaiseStateChanged();
        }
    }

    public void TriggerReminderNow()
    {
        ShowReminder(DateTimeOffset.Now, "手动测试提醒已弹出");
    }

    public void TriggerMessageNotificationNow()
    {
        var reminderTime = DateTimeOffset.Now;
        ShowPersistentToast(reminderTime, "测试消息通知");

        LastActionText = $"最后动作：已发送测试通知（{reminderTime.LocalDateTime:yyyy-MM-dd HH:mm:ss}）";
        RaiseStateChanged();
    }

    private void HourlyTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        _hourlyTimer.Stop();
        NextReminderAt = null;
        ShowReminder(DateTimeOffset.Now, "定时提醒已发送，等待点击“我去休息”");
    }

    private void StartCountdown(DateTimeOffset fromTime, string actionText)
    {
        _hourlyTimer.Interval = ReminderInterval;
        NextReminderAt = fromTime + ReminderInterval;
        LastActionText = $"{actionText}，下一次提醒在 {NextReminderAt?.LocalDateTime:yyyy-MM-dd HH:mm:ss}";

        if (_hourlyTimer.IsRunning)
        {
            _hourlyTimer.Stop();
        }

        if (IsRunning)
        {
            _hourlyTimer.Start();
        }

        RaiseStateChanged();
    }

    private void ShowReminder(DateTimeOffset reminderTime, string actionText)
    {
        CurrentReminderAt = reminderTime;
        StartTimeout(reminderTime);
        StartPersistentNotification(reminderTime, "定时提醒");

        LastActionText = $"最后动作：{actionText}";
        RaiseStateChanged();
    }

    private void StartTimeout(DateTimeOffset reminderTime)
    {
        _timeoutCts?.Cancel();
        _timeoutCts?.Dispose();
        _timeoutCts = new CancellationTokenSource();

        _ = WatchReminderTimeoutAsync(reminderTime, _timeoutCts.Token);
    }

    private async Task WatchReminderTimeoutAsync(DateTimeOffset reminderTime, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ReminderTimeout, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await ShowTimeoutToastAsync(reminderTime);
    }

    private async Task ShowTimeoutToastAsync(DateTimeOffset reminderTime)
    {
        await _dispatcherQueue.EnqueueAsync(() =>
        {
            StartPersistentNotification(reminderTime, "超时提醒");

            LastActionText = $"最后动作：提醒在 {reminderTime.LocalDateTime:yyyy-MM-dd HH:mm:ss} 超时，已发送闹钟式通知";
            RaiseStateChanged();
        });
    }

    private void PersistentNotificationTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_persistentReminderTime is not DateTimeOffset reminderTime)
        {
            return;
        }

        ShowPersistentToast(reminderTime, "持续提醒");
    }

    private void StartPersistentNotification(DateTimeOffset reminderTime, string reason)
    {
        _persistentReminderTime = reminderTime;
        ShowPersistentToast(reminderTime, reason);

        if (!_persistentNotificationTimer.IsRunning)
        {
            _persistentNotificationTimer.Start();
        }
    }

    private void StopPersistentNotification()
    {
        _persistentReminderTime = null;
        if (_persistentNotificationTimer.IsRunning)
        {
            _persistentNotificationTimer.Stop();
        }

        _ = AppNotificationManager.Default.RemoveByTagAndGroupAsync(ReminderNotificationTag, ReminderNotificationGroup);
    }

    private static void ShowPersistentToast(DateTimeOffset reminderTime, string reason)
    {
        var notification = new AppNotificationBuilder()
            .AddText("休息提醒")
            .AddText($"{reason}：提醒时间 {reminderTime.LocalDateTime:yyyy-MM-dd HH:mm:ss}")
            .AddText("请点击“我去休息”开始休息，或点击“稍后”在 5 分钟后再提醒。")
            .SetTag(ReminderNotificationTag)
            .SetGroup(ReminderNotificationGroup)
            .SetScenario(AppNotificationScenario.Alarm)
            .SetDuration(AppNotificationDuration.Long)
            .AddButton(new AppNotificationButton("我去休息").AddArgument("action", StartBreakAction))
            .AddButton(new AppNotificationButton("稍后").AddArgument("action", SnoozeAction))
            .BuildNotification();

        AppNotificationManager.Default.Show(notification);
    }

    private void AppNotificationManager_NotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        if (!args.Arguments.TryGetValue("action", out var action))
        {
            return;
        }

        _ = _dispatcherQueue.EnqueueAsync(() =>
        {
            if (string.Equals(action, StartBreakAction, StringComparison.Ordinal))
            {
                BeginBreak("最后动作：已点击“我去休息”，等待点击“我回来了”", true);
                return;
            }

            if (string.Equals(action, SnoozeAction, StringComparison.Ordinal))
            {
                SnoozeReminder();
                return;
            }

            if (string.Equals(action, ReturnAction, StringComparison.Ordinal))
            {
                ConfirmAndRestart("最后动作：已点击“我回来了”，重新开始计时");
            }
        });
    }

    private void SnoozeReminder()
    {
        _timeoutCts?.Cancel();
        StopPersistentNotification();
        CurrentReminderAt = null;

        if (_reminderWindow is not null)
        {
            _reminderWindow.Close();
            _reminderWindow = null;
        }

        if (_hourlyTimer.IsRunning)
        {
            _hourlyTimer.Stop();
        }

        if (IsRunning)
        {
            _hourlyTimer.Interval = SnoozeInterval;
            NextReminderAt = DateTimeOffset.Now + SnoozeInterval;
            _hourlyTimer.Start();
            LastActionText = $"最后动作：已点击“稍后”，将在 {NextReminderAt?.LocalDateTime:yyyy-MM-dd HH:mm:ss} 再次提醒";
            RaiseStateChanged();
            return;
        }

        LastActionText = "最后动作：已点击“稍后”";
        RaiseStateChanged();
    }

    private void ReminderWindow_BreakStarted(object? sender, EventArgs e)
    {
        BeginBreak("最后动作：已点击“我去休息”，等待点击“我回来了”", false);
    }

    private void ReminderWindow_Returned(object? sender, EventArgs e)
    {
        ConfirmAndRestart("最后动作：已点击“我回来了”，重新开始计时");
    }

    private void BeginBreak(string actionText, bool activateWindow)
    {
        _timeoutCts?.Cancel();
        StopPersistentNotification();
        
        if (_hourlyTimer.IsRunning)
        {
            _hourlyTimer.Stop();
        }

        if (_reminderWindow is null)
        {
            _reminderWindow = new ReminderWindow();
            _reminderWindow.BreakStarted += ReminderWindow_BreakStarted;
            _reminderWindow.Returned += ReminderWindow_Returned;
            _reminderWindow.Closed += ReminderWindow_Closed;
            _reminderWindow.SetReminderTime(CurrentReminderAt ?? DateTimeOffset.Now);
        }

        _reminderWindow.EnterBreakMode();

        if (activateWindow)
        {
            _reminderWindow.Activate();
        }

        LastActionText = actionText;
        RaiseStateChanged();
    }

    private void ConfirmAndRestart(string actionText)
    {
        _timeoutCts?.Cancel();
        CurrentReminderAt = null;
        StopPersistentNotification();

        if (_reminderWindow is not null)
        {
            _reminderWindow.Close();
            _reminderWindow = null;
        }

        if (IsRunning)
        {
            StartCountdown(DateTimeOffset.Now, actionText);
            return;
        }

        LastActionText = actionText;
        RaiseStateChanged();
    }

    private void ReminderWindow_Closed(object? sender, WindowEventArgs e)
    {
        _reminderWindow = null;
        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _hourlyTimer.Stop();
        _hourlyTimer.Tick -= HourlyTimer_Tick;
        _persistentNotificationTimer.Stop();
        _persistentNotificationTimer.Tick -= PersistentNotificationTimer_Tick;
        _timeoutCts?.Cancel();
        _timeoutCts?.Dispose();
        _reminderWindow?.Close();
        _reminderWindow = null;

        if (_toastRegistered)
        {
            AppNotificationManager.Default.NotificationInvoked -= AppNotificationManager_NotificationInvoked;
            StopPersistentNotification();
            AppNotificationManager.Default.Unregister();
            _toastRegistered = false;
        }
    }
}

internal static class DispatcherQueueExtensions
{
    public static Task EnqueueAsync(this DispatcherQueue dispatcherQueue, Action action)
    {
        var completionSource = new TaskCompletionSource();

        _ = dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                action();
                completionSource.SetResult();
            }
            catch (Exception exception)
            {
                completionSource.SetException(exception);
            }
        });

        return completionSource.Task;
    }
}