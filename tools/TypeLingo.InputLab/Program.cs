namespace TypeLingo.InputLab;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var form = new Form { Text = "TypeLingo InputLab · Windows 验收输入框", Width = 720, Height = 650, AutoScaleMode = AutoScaleMode.Dpi };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(20) };
        panel.Controls.Add(new Label { Text = "仅将 TypeLingo.InputLab 加入允许列表。输入中文后停顿，再用热键确认替换。", Width = 630, Height = 45 });
        void Add(string label, TextBox input)
        {
            panel.Controls.Add(new Label { Text = label, Width = 630, Height = 26 });
            input.Width = 630; input.AccessibleName = label; panel.Controls.Add(input);
        }
        Add("输入框 A · 多行", new TextBox { Multiline = true, Height = 110, ScrollBars = ScrollBars.Vertical });
        Add("输入框 B · 切换焦点", new TextBox());
        Add("Password · 必须跳过，不得读取", new TextBox { UseSystemPasswordChar = true });
        Add("只读 · 必须跳过", new TextBox { ReadOnly = true, Text = "Readonly: no translation, no replacement" });
        panel.Controls.Add(new Label { Text = "此测试窗口没有发送消息或提交表单的功能。用于检查 UIA、密码排除、焦点、防抖与确认流程；不会模拟 API。", Width = 630, Height = 60 });
        form.Controls.Add(panel); Application.Run(form);
    }
}
