using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Domain.Shared;

namespace Clube.Adapters.Http;

// The wire form is opaque on purpose: clients pass it back untouched, so the core can change what a cursor holds.
internal static class StatementCursorCodec
{
    public static string Encode(StatementCursor cursor) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(cursor.Position.ToString(CultureInfo.InvariantCulture)));

    public static Result<StatementCursor> Decode(string text)
    {
        // TryDecodeFromChars still throws on illegal characters, so validate first.
        if (!Base64Url.IsValid(text)) return MembershipErrors.InvalidCursor;

        byte[] buffer = new byte[Base64Url.GetMaxDecodedLength(text.Length)];
        if (!Base64Url.TryDecodeFromChars(text, buffer, out int written)) return MembershipErrors.InvalidCursor;

        string digits = Encoding.UTF8.GetString(buffer, 0, written);
        bool isPosition = long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long position) && position > 0;
        return isPosition ? new StatementCursor(position) : MembershipErrors.InvalidCursor;
    }
}
