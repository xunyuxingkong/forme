namespace Forme.Core;

internal sealed class BoundedReadStream(Stream inner,long byteLimit):Stream
{
    private long _read;
    public override bool CanRead=>inner.CanRead;
    public override bool CanSeek=>false;
    public override bool CanWrite=>false;
    public override long Length=>throw new NotSupportedException();
    public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
    public override void Flush(){}
    public override int Read(byte[] buffer,int offset,int count)=>Count(inner.Read(buffer,offset,count));
    public override int Read(Span<byte> buffer)=>Count(inner.Read(buffer));
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default)
        =>Count(await inner.ReadAsync(buffer,cancellationToken).ConfigureAwait(false));
    public override async Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken cancellationToken)
        =>await ReadAsync(buffer.AsMemory(offset,count),cancellationToken).ConfigureAwait(false);
    private int Count(int count)
    {
        if(_read+count>byteLimit)throw new InvalidDataException($"服务原始流式响应超过本地{byteLimit/1024/1024}MB接收上限，已停止；若启用了模型思考，请降低思考强度后手动重试。动作未执行。");
        _read+=count;return count;
    }
    public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
    public override void SetLength(long value)=>throw new NotSupportedException();
    public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    protected override void Dispose(bool disposing){if(disposing)inner.Dispose();base.Dispose(disposing);}
}
