using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using TypeLingo.App.Windows;
using TypeLingo.Core;

namespace TypeLingo.App;

/// <summary>The selection is captured before this window can gain focus; typing never recaptures our own input.</summary>
public partial class AssistantWindow : Window
{
    private readonly Func<AppSettings> settings;
    private readonly Action<Preview>? saveFavorite;
    private Preview? current;
    public InputSnapshot? SessionSnapshot { get; private set; }
    public bool HasSession => SessionSnapshot != null;
    public bool IsQuerying { get; private set; }
    public long QueryRevision { get; private set; }
    public event Action<InputSnapshot, TranslationRequest>? QueryRequested;
    public event Action? Dismissed;

    public AssistantWindow(Func<AppSettings> settings, Action<Preview>? saveFavorite = null)
    {
        this.settings = settings; this.saveFavorite = saveFavorite;
        InitializeComponent();
        Closing += (_, e) => { e.Cancel = true; CancelSession(); };
        SourceInitialized += (_, _) => Native.ApplyWindowAppearance(new WindowInteropHelper(this).Handle, DesktopTheme.IsDark);
    }
    public bool Matches(InputSnapshot snapshot) => SessionSnapshot?.SameContent(snapshot) == true;
    public void Present(InputSnapshot snapshot, bool autoRun = false, bool activate = false)
    {
        QueryRevision++; SessionSnapshot = snapshot; current = null;
        var config = settings();
        SelectionText.Text = snapshot.Text; QuestionInput.Clear(); AnswerText.Clear();
        AnswerText.Visibility = Visibility.Collapsed; EmptyText.Visibility = Visibility.Visible;
        TaskInput.ItemsSource = AssistantPresets.For(config.Assistant).Select(x => x with { Name = UiText.Get(x.Name) }).ToArray();
        TaskInput.SelectedValue = config.Assistant.PresetId;
        SetBusy(false); QueryStatus.Text = UiText.Get("选区已保留，输入问题不会读取其他内容。");
        CopyAnswerButton.IsEnabled = SaveAnswerButton.IsEnabled = false;
        _ = new WindowInteropHelper(this).EnsureHandle();
        Native.ApplyWindowAppearance(new WindowInteropHelper(this).Handle, DesktopTheme.IsDark);
        Position(snapshot.Bounds);
        if (!autoRun || config.Overlay.ShowLoading) Show();
        if (activate) { Show(); Activate(); QuestionInput.Focus(); }
        if (autoRun) Submit();
    }
    private void Position(InputRect anchor)
    {
        var source = PresentationSource.FromVisual(this);
        var scale = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)anchor.X, (int)anchor.Y)).WorkingArea;
        var topLeft = scale.Transform(new Point(area.Left, area.Top));
        var bottomRight = scale.Transform(new Point(area.Right, area.Bottom));
        Width = Math.Min(500, bottomRight.X - topLeft.X); Height = Math.Min(660, bottomRight.Y - topLeft.Y);
        MinWidth = Math.Min(360, Width); MinHeight = Math.Min(420, Height);
        var point = scale.Transform(new Point(anchor.X + anchor.Width + 12, anchor.Y));
        Left = Math.Clamp(point.X, topLeft.X, Math.Max(topLeft.X, bottomRight.X - Width));
        Top = Math.Clamp(point.Y, topLeft.Y, Math.Max(topLeft.Y, bottomRight.Y - Height));
    }
    private void Task_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TaskDescription == null) return;
        TaskDescription.Text = UiText.Get(TaskInput.SelectedValue as string == "translate"
            ? "翻译模式：沿用翻译方向、离线优先和自动积累设置。"
            : "在线 AI 任务：选区作为参考内容，提示词和补充问题决定回答方式。");
    }
    private void Submit()
    {
        if (SessionSnapshot == null || IsQuerying) return;
        try
        {
            var request = AssistantPresets.CreateRequest(settings(), SessionSnapshot.Text, TaskInput.SelectedValue as string, QuestionInput.Text);
            current = null; AnswerText.Clear(); AnswerText.Visibility = Visibility.Collapsed; EmptyText.Visibility = Visibility.Visible;
            CopyAnswerButton.IsEnabled = SaveAnswerButton.IsEnabled = false;
            QueryRevision++; SetBusy(true); QueryStatus.Text = UiText.Get("正在查询…");
            QueryRequested?.Invoke(SessionSnapshot, request);
        }
        catch (ArgumentException e) { QueryStatus.Text = UiText.Get(e.Message); }
    }
    public void Display(Preview? preview)
    {
        if (preview == null || SessionSnapshot?.SameContent(preview.Snapshot) != true) return;
        current = preview;
        AnswerText.Text = preview.Result.Text; AnswerText.FontSize = settings().Overlay.FontSize;
        AnswerText.Visibility = Visibility.Visible; EmptyText.Visibility = Visibility.Collapsed;
        CopyAnswerButton.IsEnabled = true; SaveAnswerButton.IsEnabled = saveFavorite != null;
        SetBusy(false);
        QueryStatus.Text = UiText.Get(preview.Origin == TranslationOrigin.Vocabulary ? "离线题库 · 未调用 API" :
            preview.FromCache ? "本地缓存 · 未调用 API" : preview.Request.IsTranslation ? "在线翻译" : "回答就绪 · AI 生成");
        if (!IsVisible) { Position(preview.Snapshot.Bounds); Show(); }
    }
    public void ShowStatus(string status)
    {
        if (!HasSession) return;
        QueryStatus.Text = UiText.Get(status);
    }
    public void CompleteQuery()
    {
        SetBusy(false);
        // The result-only option also needs to reveal authentication, offline-miss and other failures.
        if (HasSession && !IsVisible) Show();
    }
    private void SetBusy(bool busy)
    {
        IsQuerying = busy; QueryProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        TaskInput.IsEnabled = QuestionInput.IsEnabled = SubmitButton.IsEnabled = !busy;
    }
    public void CancelSession(bool notify = true)
    {
        QueryRevision++; SessionSnapshot = null; current = null; SetBusy(false); Hide();
        if (notify) Dismissed?.Invoke();
    }
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; CancelSession(); }
        else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; Submit(); }
    }
    private void Submit_Click(object sender, RoutedEventArgs e) => Submit();
    private void Dismiss_Click(object sender, RoutedEventArgs e) => CancelSession();
    public void CopyAnswer()
    {
        if (current == null) return;
        try { System.Windows.Clipboard.SetText(current.Result.Text); QueryStatus.Text = UiText.Get("结果已复制"); }
        catch (System.Runtime.InteropServices.COMException) { QueryStatus.Text = UiText.Get("剪贴板正忙，请重试。"); }
    }
    private void Copy_Click(object sender, RoutedEventArgs e) => CopyAnswer();
    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (current == null || saveFavorite == null) return;
        try { saveFavorite(current); SaveAnswerButton.IsEnabled = false; QueryStatus.Text = UiText.Get("已收藏，可在本地收藏与复习中查看。"); }
        catch (Exception) { QueryStatus.Text = UiText.Get("收藏失败，请检查本地存储权限。"); }
    }
}
