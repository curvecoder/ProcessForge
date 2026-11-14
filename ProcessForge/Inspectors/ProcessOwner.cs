namespace ProcessForge.Inspectors;

/// <summary>
/// Account that owns a process: display name and, on Windows, security identifier.
/// </summary>
/// <param name="UserName">Account name (<c>DOMAIN\account</c> on Windows, login on Linux); empty when unknown.</param>
/// <param name="UserSid">Security identifier on Windows; empty on Linux and when unknown.</param>
internal sealed record ProcessOwner(string UserName, string UserSid)
{
    public static ProcessOwner Unknown { get; } = new(string.Empty, string.Empty);
}
