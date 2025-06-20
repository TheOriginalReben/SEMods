// AsteroidData.cs
// Defines the data structure for cached asteroid information.

using ProtoBuf;
using VRageMath; // For Vector3D

// All classes are in the same namespace and folder, so implicit visibility should work.

namespace AsteroidGravityMod
{
    [ProtoContract]
    public class AsteroidData
    {
        [ProtoMember(1)]
        public long EntityId;

        [ProtoMember(2)]
        public Vector3D Position; // Stores the CoG after calculation, or geometric center initially

        [ProtoMember(3)]
        public double Radius;

        [ProtoMember(4)]
        public double RadiusSq;

        [ProtoMember(5)]
        public string Type;

        [ProtoMember(6)]
        public bool IsCoGCalculated; // True if precise CoG is calculated, false if using geometric center or pending

        public AsteroidData() { } // Required for ProtoBuf deserialization

        public AsteroidData(long entityId, Vector3D position, double radius, string type)
        {
            EntityId = entityId;
            Position = position;
            Radius = radius;
            RadiusSq = radius * radius;
            Type = type;
            IsCoGCalculated = false; // Initially set to false, will be true after calculation or when loaded
        }
    }
}
