using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace Launcher.Helpers;

public static class XmlHelper
{
    private static XmlSerializerNamespaces EmptyNamespace = new([XmlQualifiedName.Empty]);

    public static bool TryDeserialize<T>(string path, [NotNullWhen(true)] out T? value) where T : class
    {
        try
        {
            var xmlSerializer = new XmlSerializer(typeof(T));

            using var streamReader = new StreamReader(path);

            value = xmlSerializer.Deserialize(streamReader) as T;
        }
        catch
        {
            value = null;
        }

        return value is not null;
    }

    public static bool TrySerialize<T>(T value, string path)
    {
        try
        {
            var xmlSerializer = new XmlSerializer(typeof(T));

            using var streamWriter = new StreamWriter(path);

            xmlSerializer.Serialize(streamWriter, value, EmptyNamespace);
        }
        catch
        {
            return false;
        }

        return true;
    }

    public static bool TryDeserialize<T>(Stream stream, string schemaFileName, [NotNullWhen(true)] out T? value, [NotNullWhen(false)] out string? error) where T : class
    {
        var schema = LoadSchema(schemaFileName);

        if (schema is null)
        {
            value = null;
            error = new FileNotFoundException().Message;

            return false;
        }

        var xmlSchemaSet = new XmlSchemaSet();

        xmlSchemaSet.Add(schema);

        var xmlReaderSettings = new XmlReaderSettings
        {
            Schemas = xmlSchemaSet,
            ValidationType = ValidationType.Schema
        };

        var xmlSerializer = new XmlSerializer(typeof(T));

        using var xmlReader = XmlReader.Create(stream, xmlReaderSettings);

        try
        {
            value = xmlSerializer.Deserialize(xmlReader) as T;

            if (value is not null)
            {
                error = null;

                return true;
            }
        }
        catch (InvalidOperationException ex)
        {
            value = null;
            error = ex.InnerException is XmlSchemaValidationException validationException ? validationException.Message : ex.Message;

            return false;
        }

        value = null;
        error = new InvalidOperationException().Message;

        return false;
    }

    private static XmlSchema? LoadSchema(string fileName)
    {
        var assembly = typeof(XmlHelper).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();

        foreach (var resourceName in resourceNames)
        {
            if (!resourceName.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                continue;

            var resourceStream = assembly.GetManifestResourceStream(resourceName);

            if (resourceStream is null)
                continue;

            return XmlSchema.Read(resourceStream, null);
        }

        return null;
    }
}