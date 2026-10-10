using SS14ModLauncher.Core;

namespace SS14ModLauncher;

internal sealed class ModDetailsWindow : Form
{
    public ModDetailsWindow(ModDefinition mod, string version, string language)
    {
        string T(string ru, string en) => language == "ru" ? ru : en;
        Text = mod.Name(language); Name = "mod-details";
        BackColor = Theme.Surface; ForeColor = Theme.Text; Font = new Font("Segoe UI", 10);
        ClientSize = new Size(760, mod.PreviewImage == null ? 370 : 760);
        MinimumSize = new Size(620, mod.PreviewImage == null ? 380 : 660);
        StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi; Padding = new Padding(22);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        Controls.Add(layout);
        void Paragraph(string text, int row, float size, Color? color = null, bool bold = false)
        {
            var label = Theme.Label(text, size, color, bold); label.MaximumSize = new Size(ClientSize.Width - Padding.Horizontal - 10, 0);
            layout.SizeChanged += (_, _) => label.MaximumSize = new Size(Math.Max(100, layout.ClientSize.Width - 10), 0);
            layout.Controls.Add(label, 0, row);
        }
        Paragraph(mod.Name(language), 0, 22, bold: true);
        Paragraph("v" + version + "  ·  " + mod.CategoryName(language), 1, 9, Theme.Blue);
        Paragraph(mod.Description(language), 2, 10);
        Paragraph(T("Как пользоваться", "How to use"), 3, 11, bold: true);
        Paragraph(mod.Instructions(language), 4, 10, Theme.Muted);
        if (mod.PreviewImage is { } asset)
        {
            using var stream = typeof(ModDetailsWindow).Assembly.GetManifestResourceStream("SS14ModLauncher.Assets." + asset);
            if (stream != null)
            {
                var picture = new PictureBox { Name = "mod-preview", Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom,
                    Image = new Bitmap(stream), AccessibleName = T("Автохимия: вкладка АВТО в химмастере", "Auto Chemistry: ChemMaster AUTO tab"), Margin = new Padding(0, 8, 0, 8) };
                picture.Disposed += (_, _) => picture.Image?.Dispose(); layout.Controls.Add(picture, 0, 5);
            }
        }
        var close = Theme.Button(T("Закрыть", "Close"), (_, _) => Close()); close.Anchor = AnchorStyles.Right;
        layout.Controls.Add(close, 0, 6); AcceptButton = close; CancelButton = close;
    }
}
