using HearDelta.Core;

namespace HearDelta.App.Services;

public sealed class MeasurementAnnotationRepository(DatabaseConnectionFactory connections) : IMeasurementAnnotationRepository
{
    public IReadOnlyDictionary<Guid, MeasurementAnnotation> LoadAll()
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT measurement_id, name, comment, updated_at FROM measurement_annotations;";
        using var reader = command.ExecuteReader();
        var result = new Dictionary<Guid, MeasurementAnnotation>();
        while (reader.Read())
        {
            var annotation = new MeasurementAnnotation(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3)));
            result[annotation.MeasurementId] = annotation;
        }
        return result;
    }

    public void Save(MeasurementAnnotation annotation)
    {
        var errors = MeasurementAnnotationRules.Validate(annotation);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(annotation));
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO measurement_annotations(measurement_id, name, comment, updated_at)
            VALUES($id, $name, $comment, $updatedAt)
            ON CONFLICT(measurement_id) DO UPDATE SET name=excluded.name, comment=excluded.comment,
                updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", annotation.MeasurementId.ToString("D"));
        command.Parameters.AddWithValue("$name", annotation.Name.Trim());
        command.Parameters.AddWithValue("$comment", annotation.Comment ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", annotation.UpdatedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void Delete(Guid measurementId)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM measurement_annotations WHERE measurement_id = $id;";
        command.Parameters.AddWithValue("$id", measurementId.ToString("D"));
        command.ExecuteNonQuery();
    }
}

/// <summary>Flüchtige Ablage für Tests und Ansichten ohne Datenbank.</summary>
public sealed class InMemoryMeasurementAnnotationRepository : IMeasurementAnnotationRepository
{
    private readonly Dictionary<Guid, MeasurementAnnotation> annotations = [];

    public IReadOnlyDictionary<Guid, MeasurementAnnotation> LoadAll() => new Dictionary<Guid, MeasurementAnnotation>(annotations);

    public void Save(MeasurementAnnotation annotation)
    {
        var errors = MeasurementAnnotationRules.Validate(annotation);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(annotation));
        annotations[annotation.MeasurementId] = annotation;
    }

    public void Delete(Guid measurementId) => annotations.Remove(measurementId);
}
