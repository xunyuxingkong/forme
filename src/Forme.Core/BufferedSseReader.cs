using System.Text;

namespace Forme.Core;

internal sealed class BufferedSseReader(Stream input)
{
    private readonly byte[] _bytes=new byte[4096];
    private readonly char[] _buffer=new char[Encoding.UTF8.GetMaxCharCount(4096)];
    private readonly Decoder _decoder=Encoding.UTF8.GetDecoder();
    private int _position,_length;
    private bool _skipLf,_eof,_start=true;
    private async Task<string?> Line(CancellationToken token)
    {
        var line=new StringBuilder();
        while(true)
        {
            if(_position==_length)
            {
                // Decode exactly one available network read. StreamReader.ReadAsync can
                // greedily wait for another read after a full internal byte buffer, even
                // when it already contains a complete SSE event.
                while(!_eof)
                {
                    int count=await input.ReadAsync(_bytes.AsMemory(),token).ConfigureAwait(false);_eof=count==0;
                    _length=_decoder.GetChars(_bytes,0,count,_buffer,0,flush:_eof);_position=0;
                    if(_length>0)break;
                }
                if(_position==_length)return line.Length==0?null:line.ToString();
            }
            while(_position<_length)
            {
                char c=_buffer[_position++];
                if(_start){_start=false;if(c=='\uFEFF')continue;}
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
