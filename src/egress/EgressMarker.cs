// Marker so the architecture gate can load the egress assembly by type, not by file path.
namespace Grimoire.Egress;

/// <summary>
/// Anchors the <c>Grimoire.Egress</c> assembly for <c>tests/architecture/</c>. The proxy's own
/// composition root is top-level code and has no nameable type.
/// </summary>
public static class EgressMarker;
