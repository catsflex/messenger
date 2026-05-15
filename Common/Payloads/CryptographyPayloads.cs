namespace Common.Payloads;

public record RsaKeyPayload(string SenderName, string PublicKeyXml);

public record AesKeyPayload(string SenderName, string TargetUserName, string EncryptedAesKeyBase64, string EncryptedAesIvBase64);
