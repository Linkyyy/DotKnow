using System.Windows;
using System.Windows.Controls;
using DotKnow.Core;

namespace DotKnow.App;

public partial class SettingsWindow
{
    private readonly List<PromptPreset> customPrompts = [];
    private string? editingPromptId;
    private void LoadAssistantSettings(AppSettings settings)
    {
        customPrompts.Clear(); customPrompts.AddRange(settings.Assistant.CustomPresets);
        RefreshAssistantChoices(settings.Assistant.PresetId);
        SelectionActionInput.SelectedValue = settings.Assistant.SelectionAction;
        AnswerLanguageInput.SelectedValue = settings.Assistant.ResponseLanguage;
        PromptList.SelectedIndex = 0;
    }
    private void RefreshAssistantChoices(string? selected = null)
    {
        selected ??= AssistantTaskInput.SelectedValue as string ?? "encyclopedia";
        var presets = AssistantPresets.All.Concat(customPrompts).ToArray();
        string Label(PromptPreset x) => x.Id.StartsWith("custom-", StringComparison.Ordinal) ? x.Name : UiText.Get(x.Name);
        AssistantTaskInput.ItemsSource = presets.Select(x => new Choice(x.Id, Label(x))).ToArray();
        AssistantTaskInput.SelectedValue = presets.Any(x => x.Id == selected) ? selected : "encyclopedia";
        SelectionActionInput.ItemsSource = new[] { Option("ask", "先选择任务或输入问题（推荐）"), Option("instant", "立即执行默认预设") };
        AnswerLanguageInput.ItemsSource = new[] { Option("zh", "中文"), Option("en", "英语"), Option("ja", "日语") };
        PromptList.ItemsSource = presets.Select(x => x with { Name = Label(x) }).ToArray();
    }
    private void Prompt_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PromptNameInput == null || PromptList.SelectedItem is not PromptPreset preset) return;
        editingPromptId = preset.Id;
        PromptNameInput.Text = preset.Name; PromptBodyInput.Text = preset.Instructions;
        var custom = preset.Id.StartsWith("custom-", StringComparison.Ordinal);
        PromptNameInput.IsReadOnly = PromptBodyInput.IsReadOnly = !custom;
        StorePromptButton.IsEnabled = DeletePromptButton.IsEnabled = custom;
    }
    private void NewPrompt_Click(object sender, RoutedEventArgs e)
    {
        PromptList.SelectedItem = null; editingPromptId = "custom-" + Guid.NewGuid().ToString("N");
        PromptNameInput.Clear(); PromptBodyInput.Clear();
        PromptNameInput.IsReadOnly = PromptBodyInput.IsReadOnly = false;
        StorePromptButton.IsEnabled = true; DeletePromptButton.IsEnabled = false;
        PromptNameInput.Focus();
    }
    private void StorePrompt_Click(object sender, RoutedEventArgs e)
    {
        try { CommitPrompt(); ShowMessage("提示词已加入待保存设置，请点击保存设置。"); }
        catch (ArgumentException ex) { ShowMessage(ex.Message); }
    }
    private void CommitPrompt()
    {
        if (editingPromptId?.StartsWith("custom-", StringComparison.Ordinal) != true) return;
        var preset = new PromptPreset(editingPromptId, PromptNameInput.Text.Trim(), PromptBodyInput.Text.Trim());
        if (preset.Name.Length == 0 && preset.Instructions.Length == 0 && !customPrompts.Any(x => x.Id == preset.Id)) return;
        var proposed = customPrompts.Where(x => x.Id != preset.Id).Append(preset).ToArray();
        new AssistantSettings { CustomPresets = proposed }.Validate();
        customPrompts.Clear(); customPrompts.AddRange(proposed);
        var action = SelectionActionInput.SelectedValue; var language = AnswerLanguageInput.SelectedValue;
        RefreshAssistantChoices();
        SelectionActionInput.SelectedValue = action; AnswerLanguageInput.SelectedValue = language;
        PromptList.SelectedValue = preset.Id;
        PromptList.ScrollIntoView(PromptList.SelectedItem);
    }
    private void DeletePrompt_Click(object sender, RoutedEventArgs e)
    {
        if (editingPromptId == null || !customPrompts.Any(x => x.Id == editingPromptId)) return;
        customPrompts.RemoveAll(x => x.Id == editingPromptId);
        var action = SelectionActionInput.SelectedValue; var language = AnswerLanguageInput.SelectedValue;
        RefreshAssistantChoices(); SelectionActionInput.SelectedValue = action; AnswerLanguageInput.SelectedValue = language;
        PromptList.SelectedIndex = 0;
        ShowMessage("提示词已从待保存设置移除，请点击保存设置。");
    }
    private AssistantSettings ReadAssistantSettings()
    {
        CommitPrompt();
        return new() { PresetId = Selected(AssistantTaskInput), SelectionAction = Selected(SelectionActionInput),
            ResponseLanguage = Selected(AnswerLanguageInput), CustomPresets = customPrompts.ToArray() };
    }
    private void RefreshAssistantLanguage()
    {
        var task = Selected(AssistantTaskInput); var action = Selected(SelectionActionInput); var language = Selected(AnswerLanguageInput);
        var id = editingPromptId; var name = PromptNameInput.Text; var body = PromptBodyInput.Text;
        RefreshAssistantChoices(task); SelectionActionInput.SelectedValue = action; AnswerLanguageInput.SelectedValue = language;
        PromptList.SelectedValue = id;
        if (id?.StartsWith("custom-", StringComparison.Ordinal) == true)
        { editingPromptId = id; PromptNameInput.Text = name; PromptBodyInput.Text = body; }
    }
}
