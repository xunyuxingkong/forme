using System.Text.Json;

namespace Forme.Core;

// Only one record is buffered, including when a UTF-8 token straddles file reads.
internal sealed class JsonStreamReader(Stream stream,CancellationToken cancellation)
{
    private byte[] _buffer=new byte[8192];
    private int _offset,_length;
    private bool _final;
    private JsonReaderState _state=new(new JsonReaderOptions{MaxDepth=32});
    private Utf8JsonReader Reader()=>new(_buffer.AsSpan(_offset,_length-_offset),_final,_state);
    private void Advance(ref Utf8JsonReader reader){_offset+=(int)reader.BytesConsumed;_state=reader.CurrentState;}
    private void Fill()
    {
        cancellation.ThrowIfCancellationRequested();
        if(_final)throw new InvalidDataException("JSON 文件不完整。");
        int remaining=_length-_offset;Array.Copy(_buffer,_offset,_buffer,0,remaining);_length=remaining;_offset=0;
        if(_length==_buffer.Length)
        {
            if(_buffer.Length>=1024*1024)throw new InvalidDataException("单条导入记录超过 1MB 安全上限。");
            Array.Resize(ref _buffer,_buffer.Length*2);
        }
        int read=stream.Read(_buffer,_length,_buffer.Length-_length);_length+=read;_final=read==0;
    }
    public JsonTokenType Peek()
    {
        while(true)
        {
            var reader=Reader();if(reader.Read())return reader.TokenType;
            Advance(ref reader);if(_final)return JsonTokenType.None;Fill();
        }
    }
    public string? Read(JsonTokenType expected)
    {
        while(true)
        {
            var reader=Reader();
            if(reader.Read())
            {
                if(reader.TokenType!=expected)throw new InvalidDataException("JSON 数据结构无效。");
                string? text=expected is JsonTokenType.PropertyName or JsonTokenType.String?reader.GetString():null;
                Advance(ref reader);return text;
            }
            Advance(ref reader);Fill();
        }
    }
    public T Value<T>()
    {
        while(true)
        {
            var reader=Reader();
            if(!reader.Read()){Advance(ref reader);Fill();continue;}
            if(JsonDocument.TryParseValue(ref reader,out var document))
            {
                using(document){var value=document!.RootElement.Deserialize<T>()!;Advance(ref reader);return value;}
            }
            Fill();
        }
    }
}
