// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Security.Cryptography;
using System.Text;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Stateful OSC 5522 codec. Its limits and transaction behavior mirror Ghostty's
/// Kitty clipboard implementation while keeping application policy in callbacks.
/// </summary>
internal sealed class KittyClipboardProtocol(int maxWriteBytes)
{
    private const int MaxReadMimeTypes = 4;
    private const int MaxWriteMimeTypes = 64;
    private const int MaxAliases = 64;
    private const int MaxIdBytes = 512;
    private const int MaxMimeBytes = 256;
    private const int MaxNameBytes = 256;
    private const int MaxPasswordBytes = 128;
    private const int ReadChunkBytes = 4096;
    private const int MaxGrants = 32;
    private const string PasswordAlphabet =
        "23456789abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly List<Grant> _grants = [];
    private WriteTransaction? _write;
    private string? _pastePassword;
    private TerminalClipboardContent? _pasteContent;

    public void Reset()
    {
        _write = null;
        _grants.Clear();
        _pastePassword = null;
        _pasteContent = null;
    }

    public void Handle(
        string value,
        bool bellTerminator,
        Func<TerminalClipboardRead, TerminalClipboardReadReply>? read,
        Func<TerminalClipboardWrite, TerminalClipboardWriteReply>? write,
        Func<TerminalClipboardWrite, TerminalClipboardWriteResult>? legacyWrite,
        Action<byte[]>? respond)
    {
        int separator = value.IndexOf(';');
        string metadataText = separator < 0 ? value : value[..separator];
        string payload = separator < 0 ? string.Empty : value[(separator + 1)..];
        if (!TryParseMetadata(metadataText, out Metadata metadata, out bool invalidValue))
        {
            if (invalidValue && TryGetOperation(metadataText, out Operation invalidOperation) &&
                invalidOperation is Operation.WriteData or Operation.WriteAlias &&
                _write is not null)
            {
                FinishWrite("EINVAL", bellTerminator, respond);
            }

            return;
        }

        switch (metadata.Operation)
        {
            case Operation.Read:
                HandleRead(metadata, payload, bellTerminator, read, respond);
                break;
            case Operation.Write:
                _write = new WriteTransaction(metadata, maxWriteBytes);
                break;
            case Operation.WriteData:
                HandleWriteData(metadata, payload, bellTerminator, write, legacyWrite, respond);
                break;
            case Operation.WriteAlias:
                HandleWriteAlias(metadata, payload, bellTerminator, respond);
                break;
        }
    }

    public bool TryEncodePaste(string text, bool bracketedPaste, bool kittyPasteEvents, out byte[] sequence)
    {
        if (!kittyPasteEvents)
        {
            sequence = TerminalPasteEncoder.Encode(text, bracketedPaste);
            return sequence.Length > 0;
        }

        string password = CreatePassword();
        _pastePassword = password;
        _pasteContent = new TerminalClipboardContent("text/plain", Encoding.UTF8.GetBytes(text));
        AddGrant(password, read: true, oneTime: true);

        StringBuilder builder = new();
        AppendResponse(builder, "read", "OK", string.Empty, null, password, null, false, false);
        AppendResponse(
            builder,
            "read",
            "DATA",
            string.Empty,
            ".",
            password,
            Encoding.UTF8.GetBytes("text/plain\n"),
            false,
            false);
        AppendResponse(builder, "read", "DONE", string.Empty, null, password, null, false, false);
        sequence = Encoding.ASCII.GetBytes(builder.ToString());
        return true;
    }

    private void HandleRead(
        Metadata metadata,
        string payload,
        bool bellTerminator,
        Func<TerminalClipboardRead, TerminalClipboardReadReply>? callback,
        Action<byte[]>? respond)
    {
        if (respond is null ||
            !TryDecodeBase64Text(payload, int.MaxValue, out string requestedText))
        {
            return;
        }

        bool list = false;
        List<string> mimes = [];
        foreach (string mime in requestedText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (mime == ".")
            {
                list = true;
            }
            else if (mimes.Count < MaxReadMimeTypes)
            {
                mimes.Add(mime);
            }
        }

        string password = metadata.Name.Length == 0 ? string.Empty : metadata.Password;
        bool granted = mimes.Count > 0 && UseGrant(password, read: true);
        TerminalClipboardReadReply reply;
        if (granted && password == _pastePassword && _pasteContent is not null)
        {
            TerminalClipboardContent[] contents = mimes.Contains(_pasteContent.MimeType, StringComparer.Ordinal)
                ? [_pasteContent]
                : [];
            reply = new TerminalClipboardReadReply(
                TerminalClipboardReadResult.Success,
                contents,
                list ? [_pasteContent.MimeType] : null);
            _pastePassword = null;
            _pasteContent = null;
        }
        else if (callback is null)
        {
            reply = new TerminalClipboardReadReply(TerminalClipboardReadResult.Unsupported, []);
        }
        else
        {
            try
            {
                reply = callback(
                    new TerminalClipboardRead(
                        metadata.Location,
                        mimes,
                        list,
                        metadata.Name,
                        granted,
                        password.Length > 0));
            }
            catch
            {
                reply = new TerminalClipboardReadReply(TerminalClipboardReadResult.IoError, []);
            }
        }

        if (reply.Result != TerminalClipboardReadResult.Success)
        {
            RespondStatus(
                "read",
                MapReadStatus(reply.Result),
                metadata.Id,
                bellTerminator,
                respond);
            return;
        }

        if (reply.Remember && password.Length > 0)
        {
            AddGrant(password, read: true, oneTime: false);
        }

        StringBuilder builder = new();
        AppendResponse(
            builder,
            "read",
            "OK",
            metadata.Id,
            null,
            null,
            null,
            metadata.Location == TerminalClipboardLocation.Primary,
            bellTerminator);

        if (list)
        {
            IReadOnlyList<string> available = reply.AvailableMimeTypes ?? [];
            Span<byte> listing = stackalloc byte[ReadChunkBytes];
            int listingLength = 0;
            for (int index = 0; index < available.Count; index++)
            {
                string mime = available[index];
                int separatorLength = index == 0 ? 0 : 1;
                if (Encoding.UTF8.GetByteCount(mime) + separatorLength + 1 > listing.Length - listingLength)
                {
                    break;
                }
                if (separatorLength != 0) listing[listingLength++] = (byte)' ';
                listingLength += Encoding.UTF8.GetBytes(mime, listing[listingLength..]);
            }
            if (available.Count > 0) listing[listingLength++] = (byte)'\n';
            AppendResponse(
                builder,
                "read",
                "DATA",
                metadata.Id,
                ".",
                null,
                listing[..listingLength],
                false,
                bellTerminator);
        }

        // Responses follow requested MIME order and use the first host value
        // for each type, as Ghostty's stream handler does.
        foreach (string mime in mimes)
        {
            TerminalClipboardContent? content = null;
            foreach (TerminalClipboardContent candidate in reply.Contents)
            {
                if (candidate.MimeType == mime)
                {
                    content = candidate;
                    break;
                }
            }
            if (content is null || content.Data.Length == 0)
            {
                continue;
            }

            for (int offset = 0; offset < content.Data.Length; offset += ReadChunkBytes)
            {
                int length = Math.Min(ReadChunkBytes, content.Data.Length - offset);
                AppendResponse(
                    builder,
                    "read",
                    "DATA",
                    metadata.Id,
                    content.MimeType,
                    null,
                    content.Data.AsSpan(offset, length),
                    false,
                    bellTerminator);
            }
        }

        AppendResponse(builder, "read", "DONE", metadata.Id, null, null, null, false, bellTerminator);
        respond(Encoding.ASCII.GetBytes(builder.ToString()));
    }

    private void HandleWriteData(
        Metadata metadata,
        string payload,
        bool bellTerminator,
        Func<TerminalClipboardWrite, TerminalClipboardWriteReply>? callback,
        Func<TerminalClipboardWrite, TerminalClipboardWriteResult>? legacyCallback,
        Action<byte[]>? respond)
    {
        if (_write is null)
        {
            return;
        }

        if (metadata.Mime.Length == 0)
        {
            CommitWrite(bellTerminator, callback, legacyCallback, respond);
            return;
        }

        if (!_write.TryAppend(metadata.Mime, payload))
        {
            FinishWrite(_write.LimitExceeded ? "EFBIG" : "EINVAL", bellTerminator, respond);
        }
    }

    private void HandleWriteAlias(
        Metadata metadata,
        string payload,
        bool bellTerminator,
        Action<byte[]>? respond)
    {
        if (_write is null)
        {
            return;
        }

        if (metadata.Mime.Length == 0 ||
            !TryDecodeBase64Text(payload, int.MaxValue, out string aliases))
        {
            FinishWrite("EINVAL", bellTerminator, respond);
            return;
        }

        _write.AddAliases(metadata.Mime, aliases);
    }

    private void CommitWrite(
        bool bellTerminator,
        Func<TerminalClipboardWrite, TerminalClipboardWriteReply>? callback,
        Func<TerminalClipboardWrite, TerminalClipboardWriteResult>? legacyCallback,
        Action<byte[]>? respond)
    {
        WriteTransaction transaction = _write!;
        if (!transaction.TryCommit(out IReadOnlyList<TerminalClipboardContent> contents))
        {
            FinishWrite("EINVAL", bellTerminator, respond);
            return;
        }

        string password = transaction.Name.Length == 0 ? string.Empty : transaction.Password;
        bool granted = UseGrant(password, read: false);
        TerminalClipboardWrite request = new(
            transaction.Location,
            contents,
            transaction.Name,
            granted,
            password.Length > 0);
        TerminalClipboardWriteReply reply;
        try
        {
            if (callback is not null)
            {
                reply = callback(request);
            }
            else if (legacyCallback is not null)
            {
                reply = new TerminalClipboardWriteReply(legacyCallback(request));
            }
            else
            {
                reply = new TerminalClipboardWriteReply(TerminalClipboardWriteResult.Unsupported);
            }
        }
        catch
        {
            reply = new TerminalClipboardWriteReply(TerminalClipboardWriteResult.IoError);
        }

        if (reply.Result == TerminalClipboardWriteResult.Success && reply.Remember && password.Length > 0)
        {
            AddGrant(password, read: false, oneTime: false);
        }

        FinishWrite(MapWriteStatus(reply.Result), bellTerminator, respond);
    }

    private void FinishWrite(string status, bool bellTerminator, Action<byte[]>? respond)
    {
        string id = _write?.Id ?? string.Empty;
        _write = null;
        RespondStatus("write", status, id, bellTerminator, respond);
    }

    private static void RespondStatus(
        string operation,
        string status,
        string id,
        bool bellTerminator,
        Action<byte[]>? respond)
    {
        if (respond is null)
        {
            return;
        }

        StringBuilder builder = new();
        AppendResponse(builder, operation, status, id, null, null, null, false, bellTerminator);
        respond(Encoding.ASCII.GetBytes(builder.ToString()));
    }

    private static void AppendResponse(
        StringBuilder builder,
        string operation,
        string status,
        string id,
        string? mime,
        string? password,
        ReadOnlySpan<byte> payload,
        bool primary,
        bool bellTerminator)
    {
        builder.Append("\u001b]5522;type=").Append(operation).Append(":status=").Append(status);
        if (primary)
        {
            builder.Append(":loc=primary");
        }
        if (id.Length > 0)
        {
            builder.Append(":id=").Append(id);
        }
        if (mime is not null)
        {
            builder.Append(":mime=").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(mime)));
        }
        if (password is not null)
        {
            builder.Append(":pw=").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(password)));
        }
        if (!payload.IsEmpty)
        {
            builder.Append(';').Append(Convert.ToBase64String(payload));
        }
        builder.Append(bellTerminator ? '\u0007' : "\u001b\\");
    }

    private static bool TryParseMetadata(string value, out Metadata metadata, out bool invalidValue)
    {
        metadata = default;
        invalidValue = false;
        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        foreach (string record in value.Split(':'))
        {
            int separator = record.IndexOf('=');
            if (separator < 0)
            {
                return false;
            }
            fields[record[..separator]] = record[(separator + 1)..];
        }

        if (!fields.TryGetValue("type", out string? operationText) ||
            !TryParseOperation(operationText, out Operation operation))
        {
            return false;
        }

        string id = fields.TryGetValue("id", out string? idValue) ? SanitizeId(idValue) : string.Empty;
        if (!TryDecodeMetadata(fields, "mime", MaxMimeBytes, out string mime) ||
            !TryDecodeMetadata(fields, "name", MaxNameBytes, out string name) ||
            !TryDecodeMetadata(fields, "pw", int.MaxValue, out string password))
        {
            invalidValue = true;
            return false;
        }

        // Ghostty treats an overlong but otherwise valid password as absent.
        // Invalid base64/UTF-8 still invalidates the packet above.
        if (Encoding.UTF8.GetByteCount(password) > MaxPasswordBytes)
        {
            password = string.Empty;
        }

        metadata = new Metadata(
            operation,
            fields.TryGetValue("loc", out string? location) && location == "primary"
                ? TerminalClipboardLocation.Primary
                : TerminalClipboardLocation.Standard,
            id,
            mime,
            name,
            password);
        return true;
    }

    private static bool TryDecodeMetadata(
        IReadOnlyDictionary<string, string> fields,
        string key,
        int maxBytes,
        out string value)
    {
        if (!fields.TryGetValue(key, out string? encoded))
        {
            value = string.Empty;
            return true;
        }

        return TryDecodeBase64Text(encoded, maxBytes, out value);
    }

    private static bool TryDecodeBase64Text(string encoded, int maxBytes, out string value)
    {
        value = string.Empty;
        if (!TryDecodeBase64(encoded, maxBytes, out byte[] bytes))
        {
            return false;
        }

        try
        {
            value = StrictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool TryDecodeBase64(string encoded, int maxBytes, out byte[] bytes)
    {
        bytes = [];
        if (encoded.Length == 0)
        {
            return true;
        }
        if ((encoded.Length & 3) != 0 || encoded.Any(char.IsWhiteSpace))
        {
            return false;
        }

        int maximumLength;
        try
        {
            maximumLength = checked(encoded.Length / 4 * 3);
        }
        catch (OverflowException)
        {
            return false;
        }
        if (maximumLength > (long)maxBytes + 2)
        {
            return false;
        }

        byte[] buffer = new byte[maximumLength];
        if (!Convert.TryFromBase64Chars(encoded, buffer, out int written) || written > maxBytes)
        {
            return false;
        }
        Array.Resize(ref buffer, written);
        bytes = buffer;
        return true;
    }

    private static string SanitizeId(string value)
    {
        StringBuilder result = new(Math.Min(value.Length, MaxIdBytes));
        foreach (char character in value)
        {
            if (result.Length == MaxIdBytes)
            {
                break;
            }
            if (character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or
                (>= '0' and <= '9') or '-' or '_' or '+' or '.')
            {
                result.Append(character);
            }
        }
        return result.ToString();
    }

    private static bool TryGetOperation(string metadata, out Operation operation)
    {
        operation = default;
        bool found = false;
        foreach (string record in metadata.Split(':'))
        {
            int separator = record.IndexOf('=');
            if (separator >= 0 && record.AsSpan(0, separator).SequenceEqual("type"))
            {
                found = TryParseOperation(record[(separator + 1)..], out operation);
            }
        }
        return found;
    }

    private static bool TryParseOperation(string value, out Operation operation)
    {
        operation = value switch
        {
            "read" => Operation.Read,
            "write" => Operation.Write,
            "wdata" => Operation.WriteData,
            "walias" => Operation.WriteAlias,
            _ => default,
        };
        return value is "read" or "write" or "wdata" or "walias";
    }

    private void AddGrant(string password, bool read, bool oneTime)
    {
        if (password.Length == 0 || Encoding.UTF8.GetByteCount(password) > MaxPasswordBytes)
        {
            return;
        }

        Grant? existing = _grants.FirstOrDefault(value => value.Password == password);
        if (existing is not null)
        {
            existing.Read |= read;
            existing.Write |= !read;
            existing.OneTime &= oneTime;
            return;
        }
        if (_grants.Count == MaxGrants)
        {
            _grants.RemoveAt(0);
        }
        _grants.Add(new Grant(password, read, !read, oneTime));
    }

    private bool UseGrant(string password, bool read)
    {
        int index = _grants.FindIndex(value => value.Password == password);
        if (index < 0)
        {
            return false;
        }

        Grant grant = _grants[index];
        bool allowed = read ? grant.Read : grant.Write;
        if (grant.OneTime)
        {
            _grants.RemoveAt(index);
        }
        return allowed;
    }

    private static string CreatePassword()
    {
        Span<char> password = stackalloc char[22];
        Span<byte> random = stackalloc byte[44];
        int length = 0;
        int unbiasedLimit = 256 / PasswordAlphabet.Length * PasswordAlphabet.Length;
        while (length < password.Length)
        {
            RandomNumberGenerator.Fill(random);
            foreach (byte value in random)
            {
                if (value >= unbiasedLimit)
                {
                    continue;
                }
                password[length++] = PasswordAlphabet[value % PasswordAlphabet.Length];
                if (length == password.Length)
                {
                    break;
                }
            }
        }
        return new string(password);
    }

    private static string MapReadStatus(TerminalClipboardReadResult result) => result switch
    {
        TerminalClipboardReadResult.Denied => "EPERM",
        TerminalClipboardReadResult.Unsupported => "ENOSYS",
        TerminalClipboardReadResult.Busy => "EBUSY",
        TerminalClipboardReadResult.IoError => "EIO",
        _ => "DONE",
    };

    private static string MapWriteStatus(TerminalClipboardWriteResult result) => result switch
    {
        TerminalClipboardWriteResult.Success => "DONE",
        TerminalClipboardWriteResult.Denied => "EPERM",
        TerminalClipboardWriteResult.Unsupported => "ENOSYS",
        TerminalClipboardWriteResult.Busy => "EBUSY",
        TerminalClipboardWriteResult.InvalidData => "EINVAL",
        TerminalClipboardWriteResult.IoError => "EIO",
        _ => "EIO",
    };

    private enum Operation
    {
        Read,
        Write,
        WriteData,
        WriteAlias,
    }

    private readonly record struct Metadata(
        Operation Operation,
        TerminalClipboardLocation Location,
        string Id,
        string Mime,
        string Name,
        string Password);

    private sealed class Grant(string password, bool read, bool write, bool oneTime)
    {
        public string Password { get; } = password;
        public bool Read { get; set; } = read;
        public bool Write { get; set; } = write;
        public bool OneTime { get; set; } = oneTime;
    }

    private sealed class WriteTransaction(Metadata metadata, int maxSize)
    {
        private readonly List<Entry> _entries = [];
        private readonly List<(string Alias, string Target)> _aliases = [];
        private Entry? _current;

        public TerminalClipboardLocation Location { get; } = metadata.Location;
        public string Id { get; } = metadata.Id;
        public string Name { get; } = metadata.Name;
        public string Password { get; } = metadata.Password;
        public int TotalBytes { get; private set; }
        public bool LimitExceeded { get; private set; }

        public bool TryAppend(string mime, string payload)
        {
            if (_current is null || _current.Mime != mime)
            {
                if (_current is not null && !_current.Finish())
                {
                    return false;
                }
                _current = _entries.FirstOrDefault(value => value.Mime == mime);
                if (_current is null)
                {
                    if (_entries.Count == MaxWriteMimeTypes)
                    {
                        return true;
                    }
                    _current = new Entry(mime);
                    _entries.Add(_current);
                }
                else
                {
                    _current.Reset();
                }
            }

            int before = _current.DataLength;
            bool success = _current.Append(payload, maxSize - TotalBytes, out bool limitExceeded);
            LimitExceeded = limitExceeded;
            TotalBytes += _current.DataLength - before;
            return success;
        }

        public void AddAliases(string target, string aliases)
        {
            foreach (string alias in aliases.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (Encoding.UTF8.GetByteCount(alias) > MaxMimeBytes)
                {
                    continue;
                }
                int existing = _aliases.FindIndex(value => value.Alias == alias);
                if (existing >= 0)
                {
                    _aliases[existing] = (alias, target);
                }
                else if (_aliases.Count < MaxAliases)
                {
                    _aliases.Add((alias, target));
                }
            }
        }

        public bool TryCommit(out IReadOnlyList<TerminalClipboardContent> contents)
        {
            contents = [];
            if (_current is not null && !_current.Finish())
            {
                return false;
            }

            List<TerminalClipboardContent> result = _entries
                .Select(value => new TerminalClipboardContent(value.Mime, value.ToArray()))
                .ToList();
            foreach ((string alias, string target) in _aliases)
            {
                TerminalClipboardContent? targetContent = result.FirstOrDefault(value => value.MimeType == target);
                if (targetContent is null)
                {
                    continue;
                }
                int existing = result.FindIndex(value => value.MimeType == alias);
                TerminalClipboardContent aliased = new(alias, targetContent.Data);
                if (existing >= 0)
                {
                    result[existing] = aliased;
                }
                else
                {
                    result.Add(aliased);
                }
            }

            contents = result;
            return true;
        }

        private sealed class Entry(string mime)
        {
            private readonly MemoryStream _data = new();
            private readonly char[] _pending = new char[4];
            private int _pendingLength;
            private bool _ended;

            public string Mime { get; } = mime;
            public int DataLength => checked((int)_data.Length);

            public bool Append(string payload, int remainingBytes, out bool limitExceeded)
            {
                limitExceeded = false;
                _ended = false;
                foreach (char character in payload)
                {
                    if (!IsBase64Character(character))
                    {
                        return false;
                    }
                }

                // Decode complete groups in batches so the runtime can use its
                // vectorized base64 implementation. Only split groups need the
                // four-character carry buffer; no string is allocated per group.
                Span<byte> decoded = stackalloc byte[3072];
                ReadOnlySpan<char> input = payload;
                while (!input.IsEmpty)
                {
                    ReadOnlySpan<char> chunk;
                    if (_pendingLength > 0 || input.Length < 4)
                    {
                        int take = Math.Min(4 - _pendingLength, input.Length);
                        input[..take].CopyTo(_pending.AsSpan(_pendingLength));
                        _pendingLength += take;
                        input = input[take..];
                        if (_pendingLength < 4)
                        {
                            return true;
                        }
                        chunk = _pending;
                        _pendingLength = 0;
                    }
                    else
                    {
                        int take = Math.Min(4096, input.Length & ~3);
                        chunk = input[..take];
                        input = input[take..];
                    }

                    if (!Convert.TryFromBase64Chars(chunk, decoded, out int written))
                    {
                        return false;
                    }
                    _ended = chunk.Contains('=');
                    if (_ended && !input.IsEmpty)
                    {
                        return false;
                    }
                    if (written > remainingBytes)
                    {
                        limitExceeded = true;
                        return false;
                    }
                    _data.Write(decoded[..written]);
                    remainingBytes -= written;
                }

                return true;
            }

            public bool Finish() => _pendingLength == 0;

            public void Reset()
            {
                _data.SetLength(0);
                _pendingLength = 0;
                _ended = false;
            }

            public byte[] ToArray() => _data.ToArray();

            private static bool IsBase64Character(char value)
                => value is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or
                    (>= '0' and <= '9') or '+' or '/' or '=';
        }
    }
}
