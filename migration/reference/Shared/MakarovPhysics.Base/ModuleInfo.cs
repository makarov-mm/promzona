namespace MakarovPhysics.Base;

/// The physics-domain enum that used to live here is gone.
///
/// Every one of the 188 modules filled it in and NOTHING read it: the last
/// reader was ListScreen, deleted on 30.08 when the flat grid replaced it. A
/// required field with no reader does not stay honest - 35 of the 42
/// mathematics modules had ended up as ComplexSystems and 32 of the 42
/// astronomy ones as ModernStat, which is not a statement about either
/// subject, it is what a field means when nobody looks at it (31.08).
///
/// Same treatment as the Status attribute in Carousel.xml: the concept no
/// longer exists, so it is gone rather than repaired. If a filter ever wants
/// domains again, they belong to the PRODUCT - a physics taxonomy cannot
/// classify a Riemann surface - and it will arrive with its reader.
/// <summary>Module identity card: shown in the carousel and the experiment header.</summary>
public sealed record ModuleInfo(
    string Id, // "DoublePendulum" - matches the project name
    string Title, // English fallback title (UI shows localized one)
    string Description, // 1-2 sentences for the carousel hint
    string Version = "0.1");
