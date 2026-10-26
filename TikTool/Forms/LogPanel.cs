namespace TikTool.Forms;

public enum LogLevel
{
    Info,
    Ok,
    Warn,
    Error
}

/// <summary>Scrolling, color-coded log output shared by the Download/Unfollow/Cookie pages.</summary>
public sealed class LogPanel : Panel
{
    private readonly FlowLayoutPanel _flow;

    public LogPanel()
    {
        BackColor = ColorTranslator.FromHtml("#0a0a0a");
        BorderStyle = BorderStyle.FixedSingle;
        AutoScroll = true;
        Padding = new Padding(10, 8, 10, 8);

        _flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = Width,
            BackColor = BackColor,
        };
        Controls.Add(_flow);
        Resize += (_, _) => _flow.Width = ClientSize.Width - Padding.Horizontal;
    }

    public void Log(string text, LogLevel level = LogLevel.Info)
    {
        if (IsDisposed)
        {
            return;
        }

        void Append()
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font("Consolas", 8.5f),
                ForeColor = ColorFor(level),
                BackColor = Color.Transparent,
                Margin = new Padding(0, 1, 0, 1),
            };
            _flow.Controls.Add(label);

            const int maxLines = 200;
            while (_flow.Controls.Count > maxLines)
            {
                _flow.Controls.RemoveAt(0);
            }

            ScrollControlIntoView(label);
        }

        if (InvokeRequired)
        {
            BeginInvoke(Append);
        }
        else
        {
            Append();
        }
    }

    public void Clear()
    {
        if (InvokeRequired)
        {
            BeginInvoke(_flow.Controls.Clear);
        }
        else
        {
            _flow.Controls.Clear();
        }
    }

    private static Color ColorFor(LogLevel level) => level switch
    {
        LogLevel.Ok => Theme.Green,
        LogLevel.Warn => ColorTranslator.FromHtml("#fbbf24"),
        LogLevel.Error => ColorTranslator.FromHtml("#f87171"),
        _ => Theme.Blue,
    };
}
