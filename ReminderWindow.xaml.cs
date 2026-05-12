using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Microsoft.UI.Dispatching;
using Windows.Graphics;

namespace RestReminder;

public sealed partial class ReminderWindow : Window
{
    private bool _returned;
    private bool _initialSizeApplied;
    private readonly DispatcherQueueTimer _breakElapsedTimer;
    private DateTimeOffset? _breakStartAt;

    public ReminderWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        SetAlwaysOnTop();

        _breakElapsedTimer = DispatcherQueue.CreateTimer();
        _breakElapsedTimer.IsRepeating = true;
        _breakElapsedTimer.Interval = TimeSpan.FromSeconds(1);
        _breakElapsedTimer.Tick += BreakElapsedTimer_Tick;

        RootLayout.Loaded += ReminderWindow_Loaded;
        Closed += ReminderWindow_Closed;
    }

    private void SetAlwaysOnTop()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
        }
    }

    private void ReminderWindow_Loaded(object sender, RoutedEventArgs e)
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
        var maxWidth = Math.Max(520, (int)(workArea.Width * 0.7));
        var maxHeight = Math.Max(340, (int)(workArea.Height * 0.7));

        var rasterizationScale = Content.XamlRoot?.RasterizationScale ?? 1.0;
        var width = (int)Math.Ceiling(desired.Width * rasterizationScale);
        var height = (int)Math.Ceiling(desired.Height * rasterizationScale);

        width = Math.Clamp(width, 520, maxWidth);
        height = Math.Clamp(height, 340, maxHeight);

        AppWindow.ResizeClient(new SizeInt32(width, height));
    }

    public void SetReminderTime(DateTimeOffset reminderTime)
    {
        ReminderTimeTextBlock.Text = $"提醒时间：{reminderTime.LocalDateTime:yyyy-MM-dd HH:mm:ss}";
        PrepareForReminder();
    }

    public event EventHandler? BreakStarted;

    public event EventHandler? Returned;

    public void EnterBreakMode()
    {
        StartBreakButton.Visibility = Visibility.Collapsed;
        ReturnButton.Visibility = Visibility.Visible;
        ReminderHintTextBlock.Text = "休息中，回来后点击“我回来了”继续计时。";

        _breakStartAt = DateTimeOffset.Now;
        BreakElapsedTextBlock.Visibility = Visibility.Visible;
        UpdateBreakElapsedText();

        if (!_breakElapsedTimer.IsRunning)
        {
            _breakElapsedTimer.Start();
        }
    }

    private void PrepareForReminder()
    {
        _returned = false;
        StartBreakButton.Visibility = Visibility.Visible;
        ReturnButton.Visibility = Visibility.Collapsed;
        ReminderHintTextBlock.Text = "请离开屏幕，活动肩颈，喝口水再回来继续。";
        _breakStartAt = null;
        BreakElapsedTextBlock.Text = "休息时长：00:00";
        BreakElapsedTextBlock.Visibility = Visibility.Collapsed;

        if (_breakElapsedTimer.IsRunning)
        {
            _breakElapsedTimer.Stop();
        }
    }

    private void StartBreak_Click(object sender, RoutedEventArgs e)
    {
        EnterBreakMode();
        BreakStarted?.Invoke(this, EventArgs.Empty);
    }

    private void Return_Click(object sender, RoutedEventArgs e)
    {
        _returned = true;
        Returned?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void ReminderWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_breakElapsedTimer.IsRunning)
        {
            _breakElapsedTimer.Stop();
        }

        if (!_returned)
        {
            // 关闭本窗口只代表用户暂时忽略，具体超时提醒由服务层继续计时。
        }
    }

    private void BreakElapsedTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        UpdateBreakElapsedText();
    }

    private void UpdateBreakElapsedText()
    {
        if (_breakStartAt is not DateTimeOffset breakStart)
        {
            BreakElapsedTextBlock.Text = "休息时长：00:00";
            return;
        }

        var elapsed = DateTimeOffset.Now - breakStart;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        BreakElapsedTextBlock.Text = $"休息时长：{elapsed:mm\\:ss}";
    }
}