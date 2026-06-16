namespace Mutannot

open System
open System.Text.RegularExpressions

module TypeAnnotator =
    let private typeNamePattern =
        Regex(
            @"^\s*type\s+(?:(?:public|private|internal|rec)\s+)*(?<name>``[^`]+``|[^\s<(=:]+)",
            RegexOptions.Compiled
        )

    let private splitLines (text: string) =
        text.Split([| "\r\n"; "\n" |], StringSplitOptions.None)

    let private getNewLine (text: string) =
        if text.Contains("\r\n") then "\r\n" else "\n"

    let private getLeadingWhitespace (line: string) =
        line |> Seq.takeWhile Char.IsWhiteSpace |> Seq.toArray |> String

    let private normalizeTypeName (typeName: string) =
        let lastSegment = typeName.Split('.') |> Array.last

        let withoutGenericArguments =
            match lastSegment.IndexOf('<') with
            | -1 -> lastSegment
            | index -> lastSegment.Substring(0, index)

        if withoutGenericArguments.StartsWith("``")
           && withoutGenericArguments.EndsWith("``")
           && withoutGenericArguments.Length >= 4 then
            withoutGenericArguments.Substring(2, withoutGenericArguments.Length - 4)
        else
            withoutGenericArguments

    let private tryGetDeclaredTypeName (line: string) =
        let typeNameMatch = typeNamePattern.Match line

        if typeNameMatch.Success then
            typeNameMatch.Groups["name"].Value |> normalizeTypeName |> Some
        else
            None

    let annotateTypeWithPatch (typeName: string) (patch: string) (sourceText: string) =
        let normalizedTypeName = normalizeTypeName typeName
        let normalizedPatch = patch.TrimEnd('\r', '\n')
        let lines = splitLines sourceText

        let matchingTypeIndices =
            lines
            |> Array.mapi (fun index line -> index, tryGetDeclaredTypeName line)
            |> Array.choose (fun (index, declaredTypeName) ->
                match declaredTypeName with
                | Some name when name = normalizedTypeName -> Some index
                | _ -> None)
            |> Array.toList

        match matchingTypeIndices with
        | [] -> Error $"Could not find type '{typeName}' in the provided source file."
        | [ typeIndex ] ->
            let indentation = getLeadingWhitespace lines[typeIndex]

            let attributeLines =
                [ yield $"{indentation}[<ShouldCatch(\"\"\""

                  yield!
                      normalizedPatch
                      |> splitLines
                      |> Array.toList
                      |> List.map (fun line -> $"{indentation}{line}")

                  yield $"{indentation}\"\"\")>]" ]

            let linesList = lines |> Array.toList
            let beforeType = linesList |> List.take typeIndex
            let typeAndAfter = linesList |> List.skip typeIndex
            let newLine = getNewLine sourceText

            beforeType @ attributeLines @ typeAndAfter
            |> String.concat newLine
            |> Ok
        | _ -> Error $"Found multiple type declarations named '{typeName}' in the provided source file."
