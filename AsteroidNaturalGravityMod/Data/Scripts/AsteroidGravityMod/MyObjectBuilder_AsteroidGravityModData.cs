// MyObjectBuilder_AsteroidGravityModData.cs
// Custom object builder for saving and loading mod settings.

using ProtoBuf;
using System.Xml.Serialization;
using VRage.Game.Components; // For MyObjectBuilder_SessionComponent

// All classes are in the same namespace and folder, so implicit visibility should work.

namespace AsteroidGravityMod
{
    [ProtoContract]
    [XmlRoot("MyObjectBuilder_AsteroidGravityModData")] // Required for XML serialization
    public class MyObjectBuilder_AsteroidGravityModData : MyObjectBuilder_SessionComponent
    {
        [ProtoMember(1)] // Changed from 2 to 1, as it's now the first (and effectively only) member
        public ModSettings Settings = new ModSettings(); // Initialize directly to ensure non-null on deserialization

        public MyObjectBuilder_AsteroidGravityModData()
        {
            // The direct initialization above handles most cases.
            // This constructor is still necessary for ProtoBuf, but less critical for initialization logic.
        }
    }
}
