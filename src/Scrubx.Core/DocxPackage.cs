using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace Scrubx.Cli;

/// <summary>
/// Vue en écriture d'un .docx : donne accès aux parties XML à modifier et recopie
/// tout le reste de l'archive à l'octet près.
/// </summary>
internal sealed class DocxPackage
{
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, XDocument> _parts = new();
    private readonly HashSet<string> _dirty = new();

    public DocxPackage(ZipArchive archive) => _archive = archive;

    public bool Contains(string partName) => _archive.GetEntry(partName) != null;

    /// <summary>Charge une partie XML existante, ou <c>null</c> si elle est absente.</summary>
    public XDocument? TryLoad(string partName)
    {
        if (_parts.TryGetValue(partName, out var cached)) return cached;

        var entry = _archive.GetEntry(partName);
        if (entry == null) return null;

        using var stream = entry.Open();
        var document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        _parts[partName] = document;
        return document;
    }

    public XDocument Load(string partName) =>
        TryLoad(partName)
        ?? throw new DocxRevisionException($"Partie '{partName}' introuvable : ce fichier n'est pas un document Word valide.");

    /// <summary>Charge une partie, ou la crée (et la marque à écrire) si elle est absente.</summary>
    public XDocument GetOrCreate(string partName, Func<XDocument> factory)
    {
        var existing = TryLoad(partName);
        if (existing != null) return existing;

        var created = factory();
        _parts[partName] = created;
        Touch(partName);
        return created;
    }

    /// <summary>Marque une partie comme modifiée : elle sera réécrite à l'enregistrement.</summary>
    public void Touch(string partName) => _dirty.Add(partName);

    public void Save(Stream destination)
    {
        using var output = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        foreach (var entry in _archive.Entries)
        {
            var created = output.CreateEntry(entry.FullName, CompressionLevel.Optimal);
            using var target = created.Open();

            if (_dirty.Contains(entry.FullName))
            {
                Write(_parts[entry.FullName], target);
            }
            else
            {
                using var original = entry.Open();
                original.CopyTo(target);
            }
        }

        var existingNames = _archive.Entries.Select(e => e.FullName).ToHashSet();
        foreach (var partName in _dirty.Where(name => !existingNames.Contains(name)).OrderBy(name => name))
        {
            var created = output.CreateEntry(partName, CompressionLevel.Optimal);
            using var target = created.Open();
            Write(_parts[partName], target);
        }
    }

    private static void Write(XDocument document, Stream target)
    {
        // DisableFormatting : la moindre indentation ajoutée modifierait le texte du document.
        document.Save(target, SaveOptions.DisableFormatting);
    }
}
