namespace AetherVault.Models;

public class TokenEntity
{
    public string artist { get; set; } = string.Empty;
    public string[] artistIds { get; set; } = [];
    public string asciiName { get; set; } = string.Empty;
    public string[] attractionLights { get; set; } = [];
    public string[] availability { get; set; } = [];
    public string[] boosterTypes { get; set; } = [];
    public string borderColor { get; set; } = string.Empty;
    public string[] colorIdentity { get; set; } = [];
    public string[] colorIndicator { get; set; } = [];
    public string[] colors { get; set; } = [];
    public double? edhrecSaltiness { get; set; }
    public string faceName { get; set; } = string.Empty;
    public string[] finishes { get; set; } = [];
    public string flavorName { get; set; } = string.Empty;
    public string flavorText { get; set; } = string.Empty;
    public string[] frameEffects { get; set; } = [];
    public string frameVersion { get; set; } = string.Empty;
    public bool? isFullArt { get; set; }
    public bool? isFunny { get; set; }
    public bool? isOversized { get; set; }
    public bool? isPromo { get; set; }
    public bool? isReprint { get; set; }
    public bool? isTextless { get; set; }
    public string[] keywords { get; set; } = [];
    public string language { get; set; } = string.Empty;
    public string layout { get; set; } = string.Empty;
    public string manaCost { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public string number { get; set; } = string.Empty;
    public string orientation { get; set; } = string.Empty;
    public string originalText { get; set; } = string.Empty;
    public string[] otherFaceIds { get; set; } = [];
    public string power { get; set; } = string.Empty;
    public string printedType { get; set; } = string.Empty;
    public string[] producedMana { get; set; } = [];
    public string[] promoTypes { get; set; } = [];
    public string[] relatedCards { get; set; } = [];
    public string securityStamp { get; set; } = string.Empty;
    public string setCode { get; set; } = string.Empty;
    public string side { get; set; } = string.Empty;
    public string signature { get; set; } = string.Empty;
    public string[] sourceProducts { get; set; } = [];
    public string[] subtypes { get; set; } = [];
    public string[] supertypes { get; set; } = [];
    public string text { get; set; } = string.Empty;
    public string toughness { get; set; } = string.Empty;
    public string type { get; set; } = string.Empty;
    public string[] types { get; set; } = [];
    public string uuid { get; set; } = string.Empty;
    public string watermark { get; set; } = string.Empty;
}
