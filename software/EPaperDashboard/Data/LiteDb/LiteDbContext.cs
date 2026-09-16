using LiteDB;
using EPaperDashboard.Models;
using EPaperDashboard.Utilities;
using EPaperDashboard.Data;
using SystemTextJson = System.Text.Json;

namespace EPaperDashboard.Data.LiteDb;

/// <summary>
/// Internal LiteDB connection context. Only used by LiteDB repository implementations.
/// Business logic must never take a direct dependency on this class.
/// </summary>
internal sealed class LiteDbContext : IPersistenceInitializer, IDisposable
{
    private readonly LiteDatabase _db;

    public LiteDbContext(IEnvironmentConfiguration environmentConfiguration)
    {
        var mapper = new BsonMapper();

        // Register strongly-typed ID types — stored as ObjectId values in the database.
        // Empty IDs are stored as ObjectId.Empty. The MigrateNullObjectIds method below
        // fixes any existing null/BsonNull values from older schema versions.
        mapper.RegisterType<UserId>(
            serialize: id => new BsonValue(string.IsNullOrEmpty(id.Value) ? ObjectId.Empty : new ObjectId(id.Value)),
            deserialize: bson => bson == null || bson.IsNull || bson.AsObjectId == ObjectId.Empty ? UserId.Empty : new UserId(bson.AsObjectId.ToString()));
        mapper.RegisterType<DashboardId>(
            serialize: id => new BsonValue(string.IsNullOrEmpty(id.Value) ? ObjectId.Empty : new ObjectId(id.Value)),
            deserialize: bson => bson == null || bson.IsNull || bson.AsObjectId == ObjectId.Empty ? DashboardId.Empty : new DashboardId(bson.AsObjectId.ToString()));
        mapper.RegisterType<DeviceId>(
            serialize: id => new BsonValue(string.IsNullOrEmpty(id.Value) ? ObjectId.Empty : new ObjectId(id.Value)),
            deserialize: bson => bson == null || bson.IsNull || bson.AsObjectId == ObjectId.Empty ? DeviceId.Empty : new DeviceId(bson.AsObjectId.ToString()));
        mapper.RegisterType<PairingSessionId>(
            serialize: id => new BsonValue(string.IsNullOrEmpty(id.Value) ? ObjectId.Empty : new ObjectId(id.Value)),
            deserialize: bson => bson == null || bson.IsNull || bson.AsObjectId == ObjectId.Empty ? PairingSessionId.Empty : new PairingSessionId(bson.AsObjectId.ToString()));

        // Register custom serialization for JsonElement (must be after ID types)
        mapper.RegisterType(
            serialize: (jsonElement) => JsonElementToBsonValue(jsonElement),
            deserialize: (bsonValue) => BsonValueToJsonElement(bsonValue)
        );

        var connectionString = new ConnectionString
        {
            Filename = Path.Combine(environmentConfiguration.ConfigDir, "epaperdashboard.db"),
            Connection = ConnectionType.Direct
        };

        _db = new(connectionString, mapper);
        _db.Checkpoint();

        // Fix existing documents that have BsonNull for typed ID fields.
        // LiteDB returns null for BsonNull before calling RegisterType deserializers,
        // which causes NRE when setting struct (value-type) properties via reflection.
        MigrateNullObjectIds();
        PairingSessions.EnsureIndex(session => session.Code);
    }

    internal ILiteCollection<User> Users => _db.GetCollection<User>("users");
    internal ILiteCollection<Dashboard> Dashboards => _db.GetCollection<Dashboard>("dashboards");
    internal ILiteCollection<Device> Devices => _db.GetCollection<Device>("devices");
    internal ILiteCollection<PairingSession> PairingSessions => _db.GetCollection<PairingSession>("pairingSessions");

    internal void BeginTransaction() => _db.BeginTrans();
    internal void Commit() => _db.Commit();
    internal void Rollback() => _db.Rollback();

    public void Initialize() { } // Opening the context performs LiteDB initialization.

    public void Dispose() => _db.Dispose();

    /// <summary>
    /// Replaces BsonNull values in typed-ID fields with ObjectId.Empty across all collections.
    /// This handles documents created before the ObjectId.Empty convention was established,
    /// or documents from older schema versions that are missing newly-added fields.
    /// </summary>
    private void MigrateNullObjectIds()
    {
        // Map: collection name → list of field names that hold typed ObjectId values
        var collectionFields = new Dictionary<string, string[]>
        {
            ["users"] = ["_id"],
            ["dashboards"] = ["_id", "UserId"],
            ["devices"] = ["_id", "UserId", "DashboardId"],
            ["pairingSessions"] = ["_id", "UserId", "PendingDeviceId"],
        };

        foreach (var (collectionName, fields) in collectionFields)
        {
            var col = _db.GetCollection(collectionName);
            var docs = col.FindAll().ToList();
            foreach (var doc in docs)
            {
                var modified = false;
                foreach (var field in fields)
                {
                    if (!doc.ContainsKey(field) || doc[field].IsNull)
                    {
                        doc[field] = new BsonValue(ObjectId.Empty);
                        modified = true;
                    }
                }
                if (modified)
                {
                    col.Update(doc);
                }
            }
        }
    }

    private static BsonValue JsonElementToBsonValue(SystemTextJson.JsonElement element)
    {
        return element.ValueKind switch
        {
            SystemTextJson.JsonValueKind.Object => JsonElementToBsonDocument(element),
            SystemTextJson.JsonValueKind.Array => JsonElementToBsonArray(element),
            SystemTextJson.JsonValueKind.String => new BsonValue(element.GetString()),
            SystemTextJson.JsonValueKind.Number => element.TryGetInt32(out var i) ? new BsonValue(i) :
                                    element.TryGetInt64(out var l) ? new BsonValue(l) :
                                    new BsonValue(element.GetDouble()),
            SystemTextJson.JsonValueKind.True => new BsonValue(true),
            SystemTextJson.JsonValueKind.False => new BsonValue(false),
            SystemTextJson.JsonValueKind.Null => BsonValue.Null,
            SystemTextJson.JsonValueKind.Undefined => new BsonDocument(),
            _ => BsonValue.Null
        };
    }

    private static BsonDocument JsonElementToBsonDocument(SystemTextJson.JsonElement element)
    {
        var doc = new BsonDocument();
        foreach (var property in element.EnumerateObject())
        {
            doc[property.Name] = JsonElementToBsonValue(property.Value);
        }
        return doc;
    }

    private static BsonArray JsonElementToBsonArray(SystemTextJson.JsonElement element)
    {
        var array = new BsonArray();
        foreach (var item in element.EnumerateArray())
        {
            array.Add(JsonElementToBsonValue(item));
        }
        return array;
    }

    private static SystemTextJson.JsonElement BsonValueToJsonElement(BsonValue value)
    {
        var jsonString = BsonValueToJsonString(value);
        using var doc = SystemTextJson.JsonDocument.Parse(jsonString);
        return doc.RootElement.Clone();
    }

    private static string BsonValueToJsonString(BsonValue value)
    {
        if (value.IsNull) return "null";
        if (value.IsDocument) return value.AsDocument.ToString();
        if (value.IsArray) return value.AsArray.ToString();
        if (value.IsString) return SystemTextJson.JsonSerializer.Serialize(value.AsString);
        if (value.IsInt32) return value.AsInt32.ToString();
        if (value.IsInt64) return value.AsInt64.ToString();
        if (value.IsDouble) return value.AsDouble.ToString();
        if (value.IsBoolean) return value.AsBoolean.ToString().ToLower();
        if (value.IsDateTime) return SystemTextJson.JsonSerializer.Serialize(value.AsDateTime);
        return SystemTextJson.JsonSerializer.Serialize(value.ToString());
    }
}
