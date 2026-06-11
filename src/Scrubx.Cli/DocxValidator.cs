using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Scrubx.Cli;

public class ValidationError
{
    public string RuleName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public bool IsWarning { get; set; } = false;
}

public class ValidationReport
{
    public bool IsValid => !Errors.Any(e => !e.IsWarning);
    public List<ValidationError> Errors { get; set; } = new();
}

public class NumberingMap
{
    private readonly Dictionary<int, int> _numToAbstractMap = new();
    private readonly Dictionary<(int abstractNumId, int ilvl), string> _abstractLvlTextMap = new();

    public NumberingMap(XDocument? doc)
    {
        if (doc == null) return;
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        foreach (var numElem in doc.Descendants(w + "num"))
        {
            var numIdAttr = numElem.Attribute(w + "numId")?.Value;
            var absNumIdElem = numElem.Element(w + "abstractNumId");
            var absNumIdVal = absNumIdElem?.Attribute(w + "val")?.Value;

            if (int.TryParse(numIdAttr, out int numId) && int.TryParse(absNumIdVal, out int absNumId))
            {
                _numToAbstractMap[numId] = absNumId;
            }
        }

        foreach (var absElem in doc.Descendants(w + "abstractNum"))
        {
            var absNumIdAttr = absElem.Attribute(w + "abstractNumId")?.Value;
            if (int.TryParse(absNumIdAttr, out int absNumId))
            {
                foreach (var lvlElem in absElem.Descendants(w + "lvl"))
                {
                    var ilvlAttr = lvlElem.Attribute(w + "ilvl")?.Value;
                    var lvlTextElem = lvlElem.Element(w + "lvlText");
                    var lvlTextVal = lvlTextElem?.Attribute(w + "val")?.Value;

                    if (int.TryParse(ilvlAttr, out int ilvl) && lvlTextVal != null)
                    {
                        _abstractLvlTextMap[(absNumId, ilvl)] = lvlTextVal;
                    }
                }
            }
        }
    }

    public string GetPrefix(int numId, int ilvl)
    {
        if (_numToAbstractMap.TryGetValue(numId, out int absNumId))
        {
            if (_abstractLvlTextMap.TryGetValue((absNumId, ilvl), out var lvlText))
            {
                return lvlText;
            }
        }
        return string.Empty;
    }
}

public static class DocxValidator
{
    private static readonly XNamespace WNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static ValidationReport Validate(string docxPath)
    {
        try
        {
            using var stream = File.OpenRead(docxPath);
            return Validate(stream);
        }
        catch (Exception ex)
        {
            var report = new ValidationReport();
            report.Errors.Add(new ValidationError
            {
                RuleName = "LectureFichier",
                Message = $"Erreur lors de l'ouverture du fichier : {ex.Message}"
            });
            return report;
        }
    }

    public static ValidationReport Validate(Stream docxStream)
    {
        var report = new ValidationReport();
        
        try
        {
            using var archive = new ZipArchive(docxStream, ZipArchiveMode.Read, leaveOpen: true);
            
            XDocument? numberingDoc = null;
            var numberingEntry = archive.GetEntry("word/numbering.xml");
            if (numberingEntry != null)
            {
                using var numberingStream = numberingEntry.Open();
                numberingDoc = XDocument.Load(numberingStream);
            }
            var numberingMap = new NumberingMap(numberingDoc);

            var targetEntries = new[] { "word/document.xml", "word/footnotes.xml", "word/endnotes.xml" };
            bool hasTitre1 = false;

            foreach (var entryName in targetEntries)
            {
                var entry = archive.GetEntry(entryName);
                if (entry == null) continue;

                using var stream = entry.Open();
                var doc = XDocument.Load(stream);
                
                var paragraphElements = doc.Descendants(WNamespace + "p");
                
                foreach (var p in paragraphElements)
                {
                    var text = string.Concat(p.Descendants(WNamespace + "t").Select(e => e.Value));

                    // Check: space at the end of paragraph
                    if (text.Length > 0)
                    {
                        char lastChar = text[^1];
                        if (lastChar == ' ' || lastChar == '\u00A0' || lastChar == '\u202F' || lastChar == '\t')
                        {
                            var context = GetContext(text, text.Length - 1, 1);
                            report.Errors.Add(new ValidationError
                            {
                                RuleName = "EspaceFinParagraphe",
                                Message = "Espace en fin de paragraphe détectée.",
                                Context = context
                            });
                        }
                    }
                    // Check: two consecutive spaces (ordinary, non-breaking, or narrow non-breaking)
                    for (int idx = 0; idx < text.Length - 1; idx++)
                    {
                        char c1 = text[idx];
                        char c2 = text[idx + 1];
                        if ((c1 == ' ' || c1 == '\u00A0' || c1 == '\u202F') &&
                            (c2 == ' ' || c2 == '\u00A0' || c2 == '\u202F'))
                        {
                            int startIdx = idx;
                            while (idx < text.Length - 1 && (text[idx + 1] == ' ' || text[idx + 1] == '\u00A0' || text[idx + 1] == '\u202F'))
                            {
                                idx++;
                            }
                            int length = idx - startIdx + 1;
                            var context = GetContext(text, startIdx, length);
                            report.Errors.Add(new ValidationError
                            {
                                RuleName = "DoubleEspace",
                                Message = "Deux espaces consécutives ou plus détectées.",
                                Context = context
                            });
                        }
                    }

                    // Check: comma before "et" (warning)
                    var commaMatches = Regex.Matches(text, @",[\s\u00A0\u202F]*et\b", RegexOptions.IgnoreCase);
                    foreach (Match match in commaMatches)
                    {
                        var context = GetContext(text, match.Index, match.Length);
                        report.Errors.Add(new ValidationError
                        {
                            RuleName = "VirguleAvantEt",
                            Message = "Virgule détectée juste avant le mot 'et' (avertissement d'énumération).",
                            Context = context,
                            IsWarning = true
                        });
                    }

                    // Check page breaks: manual breaks (<w:br w:type="page"/>)
                    var manualBreaksCount = p.Descendants(WNamespace + "br")
                        .Count(br => br.Attribute(WNamespace + "type")?.Value == "page");
                    if (manualBreaksCount > 0)
                    {
                        var context = GetContext(text, 0, 0);
                        report.Errors.Add(new ValidationError
                        {
                            RuleName = "SautDePageDetecte",
                            Message = "Saut de page manuel détecté. Les sauts de page ne sont pas autorisés.",
                            Context = $"[Saut de page manuel] {context}"
                        });
                    }

                    // Check page breaks: paragraph pageBreakBefore property
                    var pageBreakBefore = p.Element(WNamespace + "pPr")?.Element(WNamespace + "pageBreakBefore");
                    if (pageBreakBefore != null)
                    {
                        var val = pageBreakBefore.Attribute(WNamespace + "val")?.Value;
                        if (val == null || (val != "false" && val != "0" && val != "off"))
                        {
                            var context = GetContext(text, 0, 0);
                            report.Errors.Add(new ValidationError
                            {
                                RuleName = "SautDePageDetecte",
                                Message = "Propriété de paragraphe 'Saut de page avant' détectée. Les sauts de page ne sont pas autorisés.",
                                Context = $"[Saut de page avant] {context}"
                            });
                        }
                    }

                    // Style checks (only for word/document.xml)
                    if (entryName == "word/document.xml")
                    {
                        var styleVal = p.Element(WNamespace + "pPr")?.Element(WNamespace + "pStyle")?.Attribute(WNamespace + "val")?.Value;
                        var styleName = styleVal ?? "Normal";

                        if (styleName == "Titre1")
                        {
                            hasTitre1 = true;
                        }

                        if (styleName != "Normal" && styleName != "Titre1" && styleName != "Ellipse")
                        {
                            var context = GetContext(text, 0, 0);
                            report.Errors.Add(new ValidationError
                            {
                                RuleName = "StyleParagrapheInvalide",
                                Message = $"Style de paragraphe non autorisé : '{styleName}'. Les seuls styles autorisés sont : 'Normal', 'Titre1', 'Ellipse'.",
                                Context = $"[Style: {styleName}] {context}"
                            });
                        }
                    }
                    
                    // Check 1: straight apostrophes
                    int aposIdx = text.IndexOf('\'');
                    while (aposIdx != -1)
                    {
                        var context = GetContext(text, aposIdx, 1);
                        report.Errors.Add(new ValidationError
                        {
                            RuleName = "ApostropheDroite",
                            Message = "Apostrophe droite (') détectée. Veuillez utiliser une apostrophe courbée (’).",
                            Context = context
                        });
                        aposIdx = text.IndexOf('\'', aposIdx + 1);
                    }

                    // Check 5: straight double quotes "
                    int quoteIdx = text.IndexOf('"');
                    while (quoteIdx != -1)
                    {
                        var context = GetContext(text, quoteIdx, 1);
                        report.Errors.Add(new ValidationError
                        {
                            RuleName = "GuillemetDroit",
                            Message = "Guillemet droit (\") détecté. Veuillez utiliser des guillemets français (« ou »).",
                            Context = context
                        });
                        quoteIdx = text.IndexOf('"', quoteIdx + 1);
                    }
                    
                    // Check 2: dash at the beginning of paragraph
                    string listPrefix = string.Empty;
                    var numPr = p.Element(WNamespace + "pPr")?.Element(WNamespace + "numPr");
                    if (numPr != null)
                    {
                        var numIdVal = numPr.Element(WNamespace + "numId")?.Attribute(WNamespace + "val")?.Value;
                        var ilvlVal = numPr.Element(WNamespace + "ilvl")?.Attribute(WNamespace + "val")?.Value;
                        
                        if (int.TryParse(numIdVal, out int numId))
                        {
                            int ilvl = 0;
                            int.TryParse(ilvlVal, out ilvl);
                            listPrefix = numberingMap.GetPrefix(numId, ilvl);
                        }
                    }

                    var trimmedText = text.TrimStart();
                    int leadingSpacesCount = text.Length - trimmedText.Length;
                    
                    bool startsWithInvalidDashText = trimmedText.Length > 0 && (trimmedText[0] == '-' || trimmedText[0] == '–');
                    bool startsWithInvalidDashList = listPrefix.Length > 0 && (listPrefix[0] == '-' || listPrefix[0] == '–');

                    if (startsWithInvalidDashText)
                    {
                        var context = GetContext(text, leadingSpacesCount, 1);
                        report.Errors.Add(new ValidationError
                        {
                            RuleName = "TiretDebutInvalide",
                            Message = $"Tiret de début de ligne invalide ({trimmedText[0]}). Veuillez utiliser un tiret cadratin (—).",
                            Context = context
                        });
                    }
                    else if (startsWithInvalidDashList)
                    {
                        var context = GetContext(text, 0, 0);
                        report.Errors.Add(new ValidationError
                        {
                            RuleName = "TiretDebutInvalide",
                            Message = $"Puce de liste de début de ligne invalide ({listPrefix}). Veuillez utiliser un tiret cadratin (—).",
                            Context = $"[Puce: {listPrefix}] >>> <<< {context}"
                        });
                    }

                    // Check 3: non-breaking space after em-dash U+2014 (—)
                    if (trimmedText.StartsWith('—'))
                    {
                        bool invalidSpace = false;
                        if (trimmedText.Length == 1)
                        {
                            invalidSpace = true;
                        }
                        else
                        {
                            char nextChar = trimmedText[1];
                            if (nextChar != '\u00A0' && nextChar != '\u202F')
                            {
                                invalidSpace = true;
                            }
                        }

                        if (invalidSpace)
                        {
                            int errIdx = leadingSpacesCount + 1;
                            var context = GetContext(text, leadingSpacesCount, 2); // Highlight the em-dash and the next char
                            report.Errors.Add(new ValidationError
                            {
                                RuleName = "EspaceInsecableManquante",
                                Message = "Tiret cadratin (—) en début de ligne non suivi d'une espace insécable.",
                                Context = context
                            });
                        }
                    }

                    // Check 4: non-breaking space before ! and ?
                    for (int idx = 0; idx < text.Length; idx++)
                    {
                        char c = text[idx];
                        if (c == '!' || c == '?')
                        {
                            if (idx > 0)
                            {
                                char prev = text[idx - 1];
                                if (prev == ' ' || prev == '\t')
                                {
                                    var context = GetContext(text, idx - 1, 2);
                                    report.Errors.Add(new ValidationError
                                    {
                                        RuleName = "EspaceInsecablePonctuation",
                                        Message = $"Espace insécable manquante avant le signe '{c}' (espace ordinaire détectée).",
                                        Context = context
                                    });
                                }
                                else if (prev != '\u00A0' && prev != '\u202F')
                                {
                                    bool requiresSpace = char.IsLetterOrDigit(prev) || 
                                                         prev == ')' || prev == ']' || prev == '}' || 
                                                         prev == '»' || prev == '”' || prev == '’' || prev == '\'';
                                    if (requiresSpace)
                                    {
                                        var context = GetContext(text, idx - 1, 2);
                                        report.Errors.Add(new ValidationError
                                        {
                                            RuleName = "EspaceInsecablePonctuation",
                                            Message = $"Espace insécable manquante avant le signe '{c}'.",
                                            Context = context
                                        });
                                    }
                                }
                            }
                        }
                    }

                    // Check 6: spacing around French quotes « and »
                    for (int idx = 0; idx < text.Length; idx++)
                    {
                        char c = text[idx];
                        if (c == '«')
                        {
                            bool invalid = false;
                            string msg = string.Empty;
                            int errLen = 1;
                            
                            if (idx == text.Length - 1)
                            {
                                invalid = true;
                                msg = "Guillemet ouvrant («) en fin de ligne non suivi d'une espace insécable.";
                            }
                            else
                            {
                                char next = text[idx + 1];
                                if (next == ' ' || next == '\t')
                                {
                                    invalid = true;
                                    msg = "Guillemet ouvrant («) non suivi d'une espace insécable (espace ordinaire détectée).";
                                    errLen = 2;
                                }
                                else if (next != '\u00A0' && next != '\u202F')
                                {
                                    invalid = true;
                                    msg = "Guillemet ouvrant («) non suivi d'une espace insécable.";
                                    errLen = 2;
                                }
                            }

                            if (invalid)
                            {
                                var context = GetContext(text, idx, errLen);
                                report.Errors.Add(new ValidationError
                                {
                                    RuleName = "EspaceGuillemet",
                                    Message = msg,
                                    Context = context
                                });
                            }
                        }
                        else if (c == '»')
                        {
                            bool invalid = false;
                            string msg = string.Empty;
                            int errIdx = idx;
                            int errLen = 1;

                            if (idx == 0)
                            {
                                invalid = true;
                                msg = "Guillemet fermant (») en début de ligne non précédé d'une espace insécable.";
                            }
                            else
                            {
                                char prev = text[idx - 1];
                                if (prev == ' ' || prev == '\t')
                                {
                                    invalid = true;
                                    msg = "Guillemet fermant (») non précédé d'une espace insécable (espace ordinaire détectée).";
                                    errIdx = idx - 1;
                                    errLen = 2;
                                }
                                else if (prev != '\u00A0' && prev != '\u202F')
                                {
                                    invalid = true;
                                    msg = "Guillemet fermant (») non précédé d'une espace insécable.";
                                    errIdx = idx - 1;
                                    errLen = 2;
                                }
                            }

                            if (invalid)
                            {
                                var context = GetContext(text, errIdx, errLen);
                                report.Errors.Add(new ValidationError
                                {
                                    RuleName = "EspaceGuillemet",
                                    Message = msg,
                                    Context = context
                                });
                            }
                        }
                    }
                }
            }

            if (!hasTitre1)
            {
                report.Errors.Add(new ValidationError
                {
                    RuleName = "StyleTitre1Manquant",
                    Message = "Le document doit contenir au moins un paragraphe portant le style 'Titre1'.",
                    Context = "[Document entier]"
                });
            }
        }
        catch (Exception ex)
        {
            report.Errors.Add(new ValidationError
            {
                RuleName = "LectureDocument",
                Message = $"Erreur lors de la lecture du document : {ex.Message}"
            });
        }

        return report;
    }

    private static string GetContext(string text, int errorIndex, int errorLength)
    {
        if (errorIndex < 0 || errorIndex > text.Length) return text;

        string textBefore = text.Substring(0, errorIndex);
        string faultyText = text.Substring(errorIndex, Math.Min(errorLength, text.Length - errorIndex));
        string textAfter = text.Substring(Math.Min(errorIndex + errorLength, text.Length));

        var wordsBefore = textBefore.Split(new[] { ' ', '\u00A0', '\u202F', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var wordsAfter = textAfter.Split(new[] { ' ', '\u00A0', '\u202F', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        string before = string.Join(" ", wordsBefore.TakeLast(5));
        string after = string.Join(" ", wordsAfter.Take(5));

        string displayFault = faultyText;
        if (displayFault.Length > 0 && displayFault.All(c => c == ' ' || c == '\u00A0' || c == '\u202F' || c == '\t'))
        {
            var parts = new List<string>();
            int stdCount = displayFault.Count(c => c == ' ');
            int nbsCount = displayFault.Count(c => c == '\u00A0');
            int nnbsCount = displayFault.Count(c => c == '\u202F');
            int tabCount = displayFault.Count(c => c == '\t');
            
            if (stdCount > 0) parts.Add($"{stdCount} espace{(stdCount > 1 ? "s" : "")} standard{(stdCount > 1 ? "s" : "")}");
            if (nbsCount > 0) parts.Add($"{nbsCount} espace{(nbsCount > 1 ? "s" : "")} insécable{(nbsCount > 1 ? "s" : "")}");
            if (nnbsCount > 0) parts.Add($"{nnbsCount} espace{(nnbsCount > 1 ? "s" : "")} insécable{(nnbsCount > 1 ? "s" : "")} fine{(nnbsCount > 1 ? "s" : "")}");
            if (tabCount > 0) parts.Add($"{tabCount} tabulation{(tabCount > 1 ? "s" : "")}");
            
            displayFault = $"[{string.Join(" + ", parts)}]";
        }
        else if (string.IsNullOrEmpty(displayFault))
        {
            displayFault = "[fin de ligne]";
        }

        return $"... {before} >>>{displayFault}<<< {after} ...";
    }
}
