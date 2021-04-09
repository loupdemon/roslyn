// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Metadata.Tools;

namespace Microsoft.CodeAnalysis.Rebuild
{
    public class RebuildResult
    {
        public RebuildResultKind Kind { get; }
        public ImmutableArray<Diagnostic> Diagnostics { get; }
        public string? ErrorMessage { get; }

        PEReader? OriginalPeBytes { get; }
        MetadataReader? OriginalPdbReader { get; }
        PEReader? RebuildPeBytes { get; }
        MetadataReader? RebuildPdbReader { get; }

        private RebuildResult(
            RebuildResultKind kind,
            ImmutableArray<Diagnostic> diagnostics = default,
            string? errorMessage = null,
            PEReader? originalPeBytes = null,
            MetadataReader? originalPdbReader = null,
            PEReader? rebuildPeBytes = null,
            MetadataReader? rebuildPdbReader = null)
        {
            Kind = kind;
            Diagnostics = diagnostics.NullToEmpty();
            ErrorMessage = errorMessage;
            OriginalPeBytes = originalPeBytes;
            OriginalPdbReader = originalPdbReader;
            RebuildPeBytes = rebuildPeBytes;
            RebuildPdbReader = rebuildPdbReader;
        }

        internal static RebuildResult Success()
        {
            return new RebuildResult(RebuildResultKind.Success);
        }

        internal static RebuildResult BinaryDifference(
            PEReader originalPeBytes,
            MetadataReader originalPdbReader,
            PEReader rebuildPeBytes,
            MetadataReader rebuildPdbReader)
        {
            return new RebuildResult(
                RebuildResultKind.BinaryDifference,
                originalPeBytes: originalPeBytes,
                originalPdbReader: rebuildPdbReader,
                rebuildPeBytes: rebuildPeBytes,
                rebuildPdbReader: rebuildPdbReader);
        }

        internal static RebuildResult CompilationError(ImmutableArray<Diagnostic> diagnostics)
        {
            return new RebuildResult(RebuildResultKind.CompilationError, diagnostics: diagnostics);
        }

        internal static RebuildResult MiscError(string message)
        {
            return new RebuildResult(RebuildResultKind.MiscError, errorMessage: message);
        }

        internal void GetBinaryDifference()
        {
            if (Kind is not RebuildResultKind.BinaryDifference)
            {
                throw new InvalidOperationException();
            }

            string getMdv(MetadataReader metadataReader)
            {
                using var stream = new MemoryStream();
                var writer = new StreamWriter(stream);

                var visualizer = new MetadataVisualizer(metadataReader, writer);
                visualizer.Visualize();
                writer.Flush();

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
