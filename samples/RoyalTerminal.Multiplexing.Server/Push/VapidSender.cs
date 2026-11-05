// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace RoyalTerminal.Multiplexing.Server;

/// <summary>RFC 8292 ES256 authorization for empty RFC 8030 messages. No endpoint or credential logging.</summary>
internal sealed class VapidSender : IDisposable
{
    private const string Header = "eyJ0eXAiOiJKV1QiLCJhbGciOiJFUzI1NiJ9";
    private readonly ECDsa _key;
    private readonly string _subject;
    private readonly HttpClient _http;
    public string PublicKey { get; }
    internal VapidSender(ECDsa key,string contact,HttpMessageHandler handler)
    {
        var p=key.ExportParameters(false);
        if(p.Curve.Oid.Value!="1.2.840.10045.3.1.7" || p.Q.X?.Length!=32 || p.Q.Y?.Length!=32) throw new ArgumentException("Web Push needs a P-256 private key.");
        if(!Uri.TryCreate(contact,UriKind.Absolute,out var u) || u.Scheme is not ("mailto" or "https") || contact.Length>512 || contact.Any(char.IsControl)) throw new ArgumentException("Set a mailto or HTTPS Web Push operator contact.");
        byte[] point=[4,..p.Q.X,..p.Q.Y]; PublicKey=WebEncoders.Base64UrlEncode(point); _key=key; _subject=contact;
        _http=new(handler){Timeout=TimeSpan.FromSeconds(5)};
    }
    internal static Uri Validate(string endpoint)
    {
        if(endpoint.Length is <16 or >2048 || endpoint.Any(c=>char.IsWhiteSpace(c)||char.IsControl(c)) || endpoint.Contains('\\') || !Uri.TryCreate(endpoint,UriKind.Absolute,out var u) || u.Scheme!="https" || u.Port!=443 || u.UserInfo.Length+u.Fragment.Length+u.Query.Length!=0 || u.AbsolutePath.Length<8)
            throw new ArgumentException("Unsupported Web Push subscription endpoint.");
        bool allowed=u.IdnHost switch {
            "fcm.googleapis.com"=>u.AbsolutePath.StartsWith("/fcm/send/",StringComparison.Ordinal)||u.AbsolutePath.StartsWith("/wp/",StringComparison.Ordinal),
            "updates.push.services.mozilla.com"=>u.AbsolutePath.StartsWith("/wpush/",StringComparison.Ordinal),
            "web.push.apple.com"=>true, _=>false };
        return allowed?u:throw new ArgumentException("Unsupported Web Push provider or path.");
    }
    internal async Task<HttpStatusCode> SendAsync(Uri subscriber,TimeProvider clock,CancellationToken ct)
    {
        var endpoint=Validate(subscriber.AbsoluteUri);
        ArrayBufferWriter<byte> bytes=new();
        using(var w=new Utf8JsonWriter(bytes)){w.WriteStartObject();w.WriteString("aud",endpoint.GetLeftPart(UriPartial.Authority));w.WriteNumber("exp",clock.GetUtcNow().AddHours(1).ToUnixTimeSeconds());w.WriteString("sub",_subject);w.WriteEndObject();}
        string claims=Header+"."+WebEncoders.Base64UrlEncode(bytes.WrittenSpan.ToArray());
        byte[] signature;lock(_key) signature=_key.SignData(Encoding.ASCII.GetBytes(claims),HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        using var r=new HttpRequestMessage(HttpMethod.Post,endpoint){Content=new ByteArrayContent([])};
        r.Headers.TryAddWithoutValidation("Authorization",$"vapid t={claims}.{WebEncoders.Base64UrlEncode(signature)}, k={PublicKey}");r.Headers.Add("TTL","60");r.Headers.TryAddWithoutValidation("Urgency","normal");
        using var response=await _http.SendAsync(r,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false); return response.StatusCode;
    }
    public void Dispose(){_http.Dispose();_key.Dispose();}
}
