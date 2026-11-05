namespace XNY.Scada.Device.Hsl;

/// <summary>HSL授权。</summary>
public static class HslLicense
{
    /// <summary>设置 HslCommunication 商业授权码，未授权时库处于试用模式</summary>
    public static bool Activate(string? code) =>
        !string.IsNullOrWhiteSpace(code) && HslCommunication.Authorization.SetAuthorizationCode(code);
}
