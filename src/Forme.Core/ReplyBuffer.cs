using System.Text;

namespace Forme.Core;

// Producer appends deltas without invoking the UI dispatcher. The UI samples at its own rate.
public sealed class ReplyBuffer
{
    private readonly StringBuilder _text=new();private readonly object _gate=new();private bool _dirty;
    public void Append(string delta){lock(_gate){_text.Append(delta);_dirty=true;}}
    public string? ReadChanges(){lock(_gate){if(!_dirty)return null;_dirty=false;return _text.ToString();}}
    public string Snapshot(){lock(_gate)return _text.ToString();}
}
