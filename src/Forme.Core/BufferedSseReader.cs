using System.Text;

namespace Forme.Core;

internal sealed class BufferedSseReader(StreamReader input)
{
    private readonly char[] _buffer=new char[4096];
    private int _position,_length,_received;
    private bool _skipLf;
    private async Task<string?> Line(CancellationToken token)
    {
        var line=new StringBuilder();
        while(true)
        {
            if(_position==_length)
            {
                _length=await input.ReadAsync(_buffer.AsMemory(),token).ConfigureAwait(false);_position=0;
                if(_length==0)return line.Length==0?null:line.ToString();
            }
            while(_position<_length)
            {
                char c=_buffer[_position++];if(++_received>256000)throw new InvalidDataException("服务响应过大，已停止接收。");
                if(_skipLf){_skipLf=false;if(c=='\n')continue;}
                if(c=='\r'){_skipLf=true;return line.ToString();}
                if(c=='\n')return line.ToString();
                line.Append(c);if(line.Length>65536)throw new InvalidDataException("服务单行响应过大。");
            }
        }
    }
    public async Task<string?> Event(CancellationToken token)
    {
        var data=new StringBuilder();bool hasData=false;
        while(await Line(token).ConfigureAwait(false) is {} line)
        {
            if(line.Length==0){if(hasData)return data.ToString();continue;}
            if(!line.StartsWith("data:",StringComparison.Ordinal))continue;
            string value=line[5..];if(value.StartsWith(' '))value=value[1..];
            if(hasData)data.Append('\n');data.Append(value);hasData=true;
            if(data.Length>65536)throw new InvalidDataException("服务单事件响应过大。");
        }
        return hasData?data.ToString():null;
    }
}
