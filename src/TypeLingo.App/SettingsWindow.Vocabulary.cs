using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TypeLingo.Infrastructure;

namespace TypeLingo.App;

public partial class SettingsWindow
{
    private readonly VocabularyStore? vocabulary;
    private readonly Action? vocabularyChanged;
    private bool vocabularyBusy;
    private int vocabularySearchRevision;
    private sealed record VocabularyRow(VocabularyEntry Entry, string Original, string Translation, string Details);
    private async Task RefreshVocabularyAsync(bool rows)
    {
        if (vocabulary == null || vocabularyBusy || VocabularyCountText == null) return;
        var revision = rows ? ++vocabularySearchRevision : vocabularySearchRevision;
        try
        {
            var counts = await vocabulary.StatisticsAsync();
            VocabularyCountText.Text = UiText.Format("可用 {0} 条 · 歧义隔离 {1} 条 · 在线积累 {2} 条 · 人工编辑 {3} 条", counts.Available, counts.Ambiguous, counts.Learned, counts.Manual);
            if (!rows) return;
            var items = await vocabulary.SearchAsync(VocabularySearchInput.Text);
            if (revision != vocabularySearchRevision) return;
            VocabularyList.ItemsSource = items.Select(entry => new VocabularyRow(entry, entry.SourceText,
                entry.IsAmbiguous ? UiText.Get("歧义条目，需人工确认后使用") : entry.TranslatedText,
                entry.SourceLanguage + " → " + entry.TargetLanguage + " · " + UiText.Get(entry.Style switch
                { "literal" => "直译", "business" => "商务翻译", "keigo" => "敬语翻译", _ => "自然翻译" }) + " · " +
                UiText.Get(entry.Origin switch { "builtin" => "内置词库", "learned" => "在线积累", "manual" => "人工编辑", _ => "导入词库" }))).ToArray();
        }
        catch (Exception) { VocabularyCountText.Text = UiText.Get("题库暂时无法读取，原有文件已保留。"); }
    }
    private void VocabularySearch_Changed(object sender, TextChangedEventArgs e)
    { if (!loading) _ = RefreshVocabularyAsync(true); }
    private void Vocabulary_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = VocabularyList.SelectedItem as VocabularyRow;
        VocabularyEditTranslation.Text = row?.Entry.TranslatedText ?? "";
        VocabularyEditButton.IsEnabled = VocabularyDeleteButton.IsEnabled = row != null;
    }
    private async void RefreshVocabulary_Click(object sender, RoutedEventArgs e) => await RefreshVocabularyAsync(true);
    private async Task RunVocabularyActionAsync(Func<Task> action, string success)
    {
        if (vocabulary == null || vocabularyBusy) return;
        vocabularyBusy = true; VocabularyManagePanel.IsEnabled = false;
        try { await Task.Run(action); vocabularyChanged?.Invoke(); ShowMessage(success); }
        catch (Exception) { ShowMessage("题库操作失败，请检查文件格式、校验值和存储权限；原有内容未被清空。"); }
        finally { vocabularyBusy = false; VocabularyManagePanel.IsEnabled = true; await RefreshVocabularyAsync(true); }
    }
    private async void ImportVocabulary_Click(object sender, RoutedEventArgs e)
    {
        if (vocabulary == null) return;
        var dialog = new OpenFileDialog { Filter = "TypeLingo (*.zip;*.jsonl)|*.zip;*.jsonl", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        VocabularyImportReport? report = null;
        await RunVocabularyActionAsync(async () => { report = await vocabulary.ImportAsync(dialog.FileName); }, "题库导入完成。");
        if (report != null) ShowMessage(UiText.Format("新增 {0} 条 · 重复 {1} 条 · 隔离冲突 {2} 条 · 无效 {3} 条 · 保留人工条目 {4} 条",
            report.Added, report.Duplicates, report.Conflicts, report.Invalid, report.Preserved));
    }
    private async void ExportVocabulary_Click(object sender, RoutedEventArgs e)
    {
        if (vocabulary == null) return;
        var dialog = new SaveFileDialog { Filter = "TypeLingo (*.zip)|*.zip", FileName = "TypeLingo-My-Vocabulary.zip", AddExtension = true };
        if (dialog.ShowDialog(this) != true || MessageBox.Show(this, UiText.Get("导出文件包含明文原文、译文与个人积累。是否继续？"), UiText.Get("导出题库"), MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
        await RunVocabularyActionAsync(() => vocabulary.ExportAsync(dialog.FileName), "题库已导出，请妥善保管明文文件。");
    }
    private async void EditVocabulary_Click(object sender, RoutedEventArgs e)
    {
        if (vocabulary == null || VocabularyList.SelectedItem is not VocabularyRow row) return;
        var text = VocabularyEditTranslation.Text;
        await RunVocabularyActionAsync(() => vocabulary.EditAsync(row.Entry, text), "译文已保存，自动积累不会覆盖人工编辑。");
    }
    private async void DeleteVocabulary_Click(object sender, RoutedEventArgs e)
    {
        if (vocabulary == null || VocabularyList.SelectedItem is not VocabularyRow row ||
            MessageBox.Show(this, UiText.Get("删除此题库条目？自动积累不会重新保存已删除的查询。"), UiText.Get("删除题库条目"), MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        await RunVocabularyActionAsync(() => vocabulary.DeleteAsync(row.Entry), "题库条目已删除。");
    }
    private async void ClearVocabulary_Click(object sender, RoutedEventArgs e)
    {
        if (vocabulary == null || MessageBox.Show(this, UiText.Get("清空题库将删除个人积累、导入和编辑，并停用内置词库。收藏和缓存保留。是否继续？"), UiText.Get("清空题库"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunVocabularyActionAsync(() => vocabulary.ClearAsync(), "题库已清空；可随时恢复内置词库。");
    }
    private async void RestoreVocabulary_Click(object sender, RoutedEventArgs e)
    {
        if (vocabulary != null) await RunVocabularyActionAsync(() => vocabulary.RestoreBuiltinAsync(), "内置词库已恢复，保留个人积累与编辑。");
    }
}
