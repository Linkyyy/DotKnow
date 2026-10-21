using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using DotKnow.App.Windows;
using DotKnow.Core;

namespace DotKnow.App;

/// <summary>The selection is captured before this window can gain focus; typing never recaptures our own input.</summary>
public partial class AssistantWindow : Window
{
    private readonly Func<AppSettings> settings;
    private readonly Action<Preview>? saveFavorite;
    private Preview? current;
    private bool questionExpanded;
    public InputSnapshot? SessionSnapshot { get; private set; }
    public bool HasSession => SessionSnapshot != null;
    internal bool OwnsForeground => Native.GetForegroundWindow() == new WindowInteropHelper(this).Handle;
    public bool IsQuerying { get; private set; }
    public long QueryRevision { get; private set; }
    public event Action<InputSnapshot, TranslationRequest>? QueryRequested;
    public event Action<InputSnapshot?>? Dismissed;

    public AssistantWindow(Func<AppSettings> settings, Action<Preview>? saveFavorite = null)
    {
        this.settings = settings; this.saveFavorite = saveFavorite;
        InitializeComponent();
        Closing += (_, e) => { e.Cancel = true; CancelSession(); };
        SourceInitialized += (_, _) => ApplyNativeTheme();
    }
    public bool Matches(InputSnapshot snapshot) => SessionSnapshot?.SameContent(snapshot) == true;
    public void Present(InputSnapshot snapshot, bool autoRun = false, bool activate = false)
    {
        QueryRevision++; SessionSnapshot = snapshot; current = null;
        var config = settings();
        questionExpanded = false;
        SelectionText.Text = snapshot.Text; QuestionInput.Clear(); AnswerText.Clear();
        SelectionPreview.Text = snapshot.Text.Replace('\r', ' ').Replace('\n', ' ');
        SourceExpander.IsExpanded = false;
        AnswerText.Visibility = Visibility.Collapsed; EmptyText.Visibility = Visibility.Visible;
        TaskInput.ItemsSource = AssistantPresets.For(config.Assistant).Select(x => x with
            { Name = x.Id.StartsWith("custom-", StringComparison.Ordinal) ? x.Name : UiText.Get(x.Name) }).ToArray();
        TaskInput.SelectedValue = config.Assistant.PresetId;
        UpdateQuestionPanel();
        EditorPanel.Visibility = Visibility.Visible; FollowUpButton.Visibility = Visibility.Collapsed;
        SetBusy(false); QueryStatus.Text = UiText.Get("选区已保留，输入问题不会读取其他内容。");
        CopyAnswerButton.IsEnabled = SaveAnswerButton.IsEnabled = false;
        _ = new WindowInteropHelper(this).EnsureHandle();
        ApplyNativeTheme();
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
        MinWidth = Math.Min(320, bottomRight.X - topLeft.X); MinHeight = Math.Min(260, bottomRight.Y - topLeft.Y);
        Width = Math.Min(460, bottomRight.X - topLeft.X);
        SelectionPreview.MaxWidth = Math.Max(100, Width - 100);
        Height = Math.Min(current != null ? 420 : QuestionPanel.Visibility == Visibility.Visible ? 430 : 340, bottomRight.Y - topLeft.Y);
        var point = scale.Transform(new Point(anchor.X + anchor.Width + 12, anchor.Y));
        Left = Math.Clamp(point.X, topLeft.X, Math.Max(topLeft.X, bottomRight.X - Width));
        Top = Math.Clamp(point.Y, topLeft.Y, Math.Max(topLeft.Y, bottomRight.Y - Height));
    }
    private void ApplyNativeTheme() => Native.ApplyWindowAppearance(new WindowInteropHelper(this).Handle,
        AppearanceTheme.IsDark(settings().Overlay.Theme, DesktopTheme.IsDark));
    private void Task_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TaskDescription == null) return;
        TaskDescription.Text = UiText.Get(TaskInput.SelectedValue as string == "translate"
            ? "翻译模式：沿用翻译方向、离线优先和自动积累设置。"
            : "在线 AI 任务：选区作为参考内容，提示词和补充问题决定回答方式。");
        TaskInput.ToolTip = TaskDescription.Text;
        TaskHeading.Text = (TaskInput.SelectedItem as PromptPreset)?.Name ?? UiText.Get("点知 · DotKnow");
        UpdateQuestionPanel();
    }
    private void UpdateQuestionPanel()
    {
        if (QuestionPanel == null) return;
        var required = TaskInput.SelectedValue as string == "ask";
        QuestionPanel.Visibility = required || questionExpanded ? Visibility.Visible : Visibility.Collapsed;
        QuestionToggleButton.Visibility = required ? Visibility.Collapsed : Visibility.Visible;
        QuestionToggleButton.Content = UiText.Get(questionExpanded ? "收起问题" : "补充问题");
    }
    private void QuestionToggle_Click(object sender, RoutedEventArgs e)
    {
        questionExpanded = !questionExpanded; UpdateQuestionPanel();
        if (SessionSnapshot != null) Position(SessionSnapshot.Bounds);
        if (questionExpanded) QuestionInput.Focus();
    }
    private void FollowUp_Click(object sender, RoutedEventArgs e)
    {
        EditorPanel.Visibility = Visibility.Visible; FollowUpButton.Visibility = Visibility.Collapsed;
        questionExpanded = true; UpdateQuestionPanel();
        if (SessionSnapshot != null) Position(SessionSnapshot.Bounds);
        Activate(); QuestionInput.Focus();
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
        EditorPanel.Visibility = Visibility.Collapsed; FollowUpButton.Visibility = Visibility.Visible;
        SourceExpander.IsExpanded = false;
        QueryStatus.Text = UiText.Get(preview.Origin == TranslationOrigin.Vocabulary ? "离线题库 · 未调用 API" :
            preview.FromCache ? "本地缓存 · 未调用 API" : preview.Request.IsTranslation ? "在线翻译" : "回答就绪 · AI 生成");
        Position(preview.Snapshot.Bounds);
        if (!IsVisible) Show();
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
        var snapshot = SessionSnapshot;
        QueryRevision++; SessionSnapshot = null; current = null; SetBusy(false); Hide();
        if (notify) Dismissed?.Invoke(snapshot);
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
