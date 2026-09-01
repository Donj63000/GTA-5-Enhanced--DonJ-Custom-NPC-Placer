using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using DonJ.JusticeRecognition;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
[DoNotParallelize]
public sealed class JusticeLegacyWarrantRecognitionContractTests
{
    private const BindingFlags PrivateInstance =
        BindingFlags.Instance | BindingFlags.NonPublic;

    [TestMethod]
    public void JudicialWarrantObservation_YieldsOnlyToTheActiveLocalRecognitionProfile()
    {
        object script = FormatterServices.GetUninitializedObject(
            typeof(DonJEnemySpawner));
        object recognition = FormatterServices.GetUninitializedObject(
            typeof(DonJJusticeRecognitionScript));
        FieldInfo bridgeInstance = typeof(JusticeRecognitionBridge).GetField(
            "_instance",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(bridgeInstance);
        object previousInstance = bridgeInstance.GetValue(null);

        // Je fournis au pont uniquement son cache thread-safe : ce test ne lit
        // aucune native et prouve qu'un module ON sans zone ne coupe pas le repli.
        SetField(recognition, "_statusSync", new object());
        SetField(recognition, "_hasActiveSearchZoneStatus", true);
        bridgeInstance.SetValue(null, recognition);

        try
        {
            SetField(script, "_justiceActivePlayerProfileSlot", 1);
            SetField(script, "_justiceRecognitionSynchronizedEnabled", (bool?)true);
            SetField(script, "_justiceRecognitionSynchronizedSuspended", (bool?)false);
            SetField(script, "_justiceRecognitionSynchronizedProfileSlot", 1);

            Assert.IsTrue(
                InvokeLocalRecognitionGate(script),
                "Une vraie zone locale du même protagoniste doit posséder l'observation monde.");

            SetField(recognition, "_hasActiveSearchZoneStatus", false);
            Assert.IsFalse(
                InvokeLocalRecognitionGate(script),
                "Un module actif sans zone locale doit laisser vivre le mandat judiciaire.");

            SetField(recognition, "_hasActiveSearchZoneStatus", true);
            SetField(script, "_justiceRecognitionSynchronizedSuspended", (bool?)true);
            Assert.IsFalse(
                InvokeLocalRecognitionGate(script),
                "Un runtime local suspendu ne doit pas neutraliser le repli judiciaire.");

            SetField(script, "_justiceRecognitionSynchronizedSuspended", (bool?)false);
            SetField(script, "_justiceRecognitionSynchronizedEnabled", (bool?)false);
            Assert.IsFalse(
                InvokeLocalRecognitionGate(script),
                "Un module local désactivé ne doit pas neutraliser le repli judiciaire.");

            SetField(script, "_justiceRecognitionSynchronizedEnabled", (bool?)true);
            SetField(script, "_justiceRecognitionSynchronizedProfileSlot", 2);
            Assert.IsFalse(
                InvokeLocalRecognitionGate(script),
                "Le mandat local d'un autre protagoniste ne doit jamais masquer le mandat actif.");

            SetField(script, "_justiceActivePlayerProfileSlot", -1);
            SetField(script, "_justiceRecognitionSynchronizedProfileSlot", -1);
            Assert.IsFalse(
                InvokeLocalRecognitionGate(script),
                "Une identité non canonique doit rester fermée et ne pas être prise pour un profil actif.");
        }
        finally
        {
            bridgeInstance.SetValue(null, previousInstance);
        }
    }

    [TestMethod]
    public void JudicialWarrantObservation_GatesBeforeScanningAndRemainsInformational()
    {
        string source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "DonJEnemySpawner",
            "DonJEnemySpawner.Justice.cs"));

        string observation = ExtractMethodBody(
            source,
            "UpdateJusticeWarrantRecognition");
        string gate = ExtractMethodBody(
            source,
            "IsJusticeLocalWarrantRecognitionActive");

        AssertContainsInOrder(
            observation,
            "IsJusticeLocalWarrantRecognitionActive()",
            "_justiceCaseState.HasWarrant",
            "GetJusticeSnapshotPeds()");

        StringAssert.Contains(
            observation,
            "mandat judiciaire actif repéré");
        Assert.IsFalse(
            observation.IndexOf(
                "SetJusticeWantedMinimum(",
                StringComparison.Ordinal) >= 0,
            "L'ancien mandat judiciaire reste informatif : seul le mandat local peut restaurer les étoiles.");
        Assert.IsFalse(
            observation.IndexOf(
                "JusticeRecognitionBridge.",
                StringComparison.Ordinal) >= 0,
            "L'ancien scan ne doit pas déclencher une seconde notification du nouveau module.");

        StringAssert.Contains(
            gate,
            "_justiceRecognitionSynchronizedEnabled == true");
        StringAssert.Contains(
            gate,
            "_justiceRecognitionSynchronizedSuspended == false");
        StringAssert.Contains(
            gate,
            "_justiceRecognitionSynchronizedProfileSlot");
        StringAssert.Contains(
            gate,
            "_justiceActivePlayerProfileSlot");
        StringAssert.Contains(
            gate,
            "JusticeRecognitionBridge");
        StringAssert.Contains(
            gate,
            ".HasActiveSearchZone()");

        // Je verrouille aussi la persistance du mandat juridique : seul son
        // ancien observateur cède la main, jamais le dossier ni son XML.
        StringAssert.Contains(
            source,
            "writer.WriteAttributeString(\"hasWarrant\", state.HasWarrant ? \"true\" : \"false\")");
    }

    private static bool InvokeLocalRecognitionGate(object script)
    {
        MethodInfo method = typeof(DonJEnemySpawner).GetMethod(
            "IsJusticeLocalWarrantRecognitionActive",
            PrivateInstance);
        Assert.IsNotNull(method);
        return (bool)method.Invoke(script, null);
    }

    private static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            PrivateInstance);
        Assert.IsNotNull(field, "Champ privé introuvable : " + fieldName);
        field.SetValue(target, value);
    }

    private static string ExtractMethodBody(string source, string methodName)
    {
        string marker = methodName + "(";
        int nameIndex = -1;
        int searchAt = 0;

        while (searchAt < source.Length)
        {
            int candidate = source.IndexOf(
                marker,
                searchAt,
                StringComparison.Ordinal);
            if (candidate < 0)
            {
                break;
            }

            int lineStart = source.LastIndexOf('\n', candidate);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            string declarationPrefix = source.Substring(
                lineStart,
                candidate - lineStart);

            if (declarationPrefix.IndexOf(
                "private ",
                StringComparison.Ordinal) >= 0)
            {
                nameIndex = candidate;
                break;
            }

            searchAt = candidate + marker.Length;
        }

        Assert.IsTrue(nameIndex >= 0, "Méthode source introuvable : " + methodName);

        int openingBrace = source.IndexOf('{', nameIndex);
        Assert.IsTrue(openingBrace >= 0, "Corps source introuvable : " + methodName);

        int depth = 0;
        for (int index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(
                        openingBrace,
                        index - openingBrace + 1);
                }
            }
        }

        Assert.Fail("Corps source non fermé : " + methodName);
        return string.Empty;
    }

    private static void AssertContainsInOrder(
        string source,
        params string[] markers)
    {
        int previous = -1;
        foreach (string marker in markers)
        {
            int current = source.IndexOf(
                marker,
                previous + 1,
                StringComparison.Ordinal);
            Assert.IsTrue(
                current > previous,
                "Marqueur absent ou ordre invalide : " + marker);
            previous = current;
        }
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(
            AppDomain.CurrentDomain.BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(
                directory.FullName,
                "GTA5modDEV.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("Impossible de retrouver la racine du dépôt.");
        return string.Empty;
    }
}
