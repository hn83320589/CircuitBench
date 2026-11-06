namespace CircuitBench.Core.Model;

/// <summary>節點名稱，比較時不分大小寫（與 SPICE 一致）。接地固定為 "0"。</summary>
public readonly struct NodeName : IEquatable<NodeName>
{
    public static readonly NodeName Ground = new("0");

    public NodeName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public bool IsGround => Value == "0";

    public bool Equals(NodeName other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is NodeName other && Equals(other);

    public override int GetHashCode() => Value is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(NodeName left, NodeName right) => left.Equals(right);

    public static bool operator !=(NodeName left, NodeName right) => !left.Equals(right);

    public static implicit operator NodeName(string value) => new(value);
}
