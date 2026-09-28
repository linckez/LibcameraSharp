namespace StreamingCamera;

/// <summary>
/// Wraps each JPEG the encoder writes in a multipart boundary, which is how a browser renders an
/// MJPEG stream in an <c>&lt;img&gt;</c>.
/// </summary>
/// <remarks>
/// This is HTTP's convention rather than video's, which is why it lives in the application and not
/// in LibcameraSharp: the library hands over frames and stops there.
/// </remarks>
internal sealed class MultipartStream(Stream inner, string boundary) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    // The encoder hands over one whole JPEG per write, so one write is one part.
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var header = System.Text.Encoding.ASCII.GetBytes(
            $"--{boundary}\r\nContent-Type: image/jpeg\r\nContent-Length: {buffer.Length}\r\n\r\n");
        inner.Write(header);
        inner.Write(buffer);
        inner.Write("\r\n"u8);
        inner.Flush();
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
