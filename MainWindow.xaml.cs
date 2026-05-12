using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace RestReminder;

public sealed partial class MainWindow : Window
{
    private readonly RestReminderService _reminderService;
    private bool _initialSizeApplied;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        RootLayout.Loaded += MainWindow_Loaded;

        _reminderService = new RestReminderService(this);
        _reminderService.StateChanged += ReminderService_StateChanged;
        _reminderService.Start();
        UpdateStatus();
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialSizeApplied)
        {
            return;
        }

        _initialSizeApplied = true;
        DispatcherQueue.TryEnqueue(ApplyAutoWindowSize);
    }

    private void ApplyAutoWindowSize()
    {
        RootLayout.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = RootLayout.DesiredSize;

        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var maxWidth = Math.Max(960, (int)(workArea.Width * 0.9));
        var maxHeight = Math.Max(680, (int)(workArea.Height * 0.9));

        var rasterizationScale = Content.XamlRoot?.RasterizationScale ?? 1.0;
        var width = (int)Math.Ceiling(desired.Width * rasterizationScale);
        var height = (int)Math.Ceiling(desired.Height * rasterizationScale);

        width = Math.Clamp(width, 960, maxWidth);
        height = Math.Clamp(height, 680, maxHeight);

        AppWindow.ResizeClient(new SizeInt32(width, height));
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _reminderService.Dispose();
    }

    private void ReminderService_StateChanged(object? sender, EventArgs e)
    {
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        IntervalNumberBox.Value = _reminderService.ReminderInterval.TotalMinutes;
        IntervalText.Text = $"提醒间隔：{_reminderService.ReminderInterval.TotalMinutes:0} 分钟";
        StatusText.Text = _reminderService.IsRunning ? "运行中" : "未启动";
        NextReminderText.Text = _reminderService.NextReminderAt is DateTimeOffset nextReminder
            ? $"下一次提醒：{nextReminder.LocalDateTime:yyyy-MM-dd HH:mm:ss}"
            : "下一次提醒：-";

        CurrentReminderText.Text = _reminderService.CurrentReminderAt is DateTimeOffset currentReminder
            ? $"当前提醒：{currentReminder.LocalDateTime:yyyy-MM-dd HH:mm:ss}"
            : "当前提醒：-";

        LastActionText.Text = _reminderService.LastActionText ?? "最后动作：-";
    }

    private void TestMessageNotificationButton_Click(object sender, RoutedEventArgs e)
    {
        _reminderService.TriggerMessageNotificationNow();
    }

    private void ApplyIntervalButton_Click(object sender, RoutedEventArgs e)
    {
        var rawMinutes = IntervalNumberBox.Value;
        if (double.IsNaN(rawMinutes) || rawMinutes < 1)
        {
            return;
        }

        var minutes = (int)Math.Round(rawMinutes);
        _reminderService.SetReminderInterval(TimeSpan.FromMinutes(minutes));
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}