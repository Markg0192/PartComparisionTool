using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PartComparisionTool.Models;
using Tekla.Structures.Model;

namespace PartComparisionTool.Services
{
    internal sealed class PartComparisonService
    {
        private const double LengthTolerance = 1.0;

        private readonly Model _model;
        private readonly AssemblySnapshotBuilder _snapshotBuilder;
        private readonly AssemblyComparer _comparer;

        private Assembly _targetAssembly;
        private AssemblySnapshot _target;

        public PartComparisonService()
        {
            _model = new Model();
            _snapshotBuilder = new AssemblySnapshotBuilder();
            _comparer = new AssemblyComparer();
        }

        public bool IsConnected => _model.GetConnectionStatus();

        public string CaptureTargetSelection()
        {
            if (!IsConnected)
                throw new InvalidOperationException("Open Tekla Structures 2023 with the required model loaded first.");

            var selectedPart = GetSingleSelectedPart();
            var assembly = selectedPart.GetAssembly();
            if (assembly == null)
                throw new InvalidOperationException("The selected part does not belong to an assembly.");

            _targetAssembly = assembly;
            _target = _snapshotBuilder.Build(assembly);

            return BuildTargetDescription(_target);
        }

        public List<ComparisonResult> FindMatchesInCurrentSelection(
            Action<SearchProgress> progress,
            out string searchDescription)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Open Tekla Structures 2023 with the required model loaded first.");

            if (_target == null || _targetAssembly == null)
                throw new InvalidOperationException("Set the target piece first.");

            progress?.Invoke(new SearchProgress { Message = "Reading selected search steel..." });

            var selectedAssemblies = GetUniqueSelectedAssemblies();
            if (selectedAssemblies.Count == 0)
                throw new InvalidOperationException("Select the steel you want to search through in Tekla first.");

            searchDescription = $"Searching {selectedAssemblies.Count:n0} selected assembly/assemblies";

            var results = new List<ComparisonResult>();
            var current = 0;
            var total = selectedAssemblies.Count;

            foreach (var assembly in selectedAssemblies)
            {
                current++;

                if (current == 1 || current % 25 == 0 || current == total)
                {
                    progress?.Invoke(new SearchProgress
                    {
                        Current = current,
                        Total = total,
                        Message = $"Checking selected assemblies {current:n0} of {total:n0}..."
                    });
                }

                if (assembly.Identifier.ID == _targetAssembly.Identifier.ID)
                    continue;

                var mainPart = assembly.GetMainPart() as Part;
                if (mainPart == null)
                    continue;

                // Keep the cheap checks first. We do not build solids, bolts or welds
                // until the candidate has the same main profile and length.
                if (!string.Equals(
                        AssemblySnapshotBuilder.GetProfile(mainPart),
                        _target.MainProfile,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var length = AssemblySnapshotBuilder.GetLength(mainPart);
                if (Math.Abs(length - _target.MainLength) > LengthTolerance)
                    continue;

                progress?.Invoke(new SearchProgress
                {
                    Current = current,
                    Total = total,
                    Message = $"Comparing candidate {SafeAssemblyMark(mainPart)}..."
                });

                try
                {
                    var candidate = _snapshotBuilder.Build(assembly);
                    results.Add(_comparer.Compare(_target, candidate));
                }
                catch
                {
                    // A malformed or unsupported object should not stop the whole search.
                }
            }

            var ordered = results
                .OrderBy(x => x.Quality)
                .ThenByDescending(x => x.Score)
                .ThenBy(x => Math.Abs(x.WeightDifference))
                .ThenBy(x => x.Phase)
                .ThenBy(x => x.AssemblyMark)
                .ToList();

            progress?.Invoke(new SearchProgress
            {
                Current = total,
                Total = total,
                Message = ordered.Count == 0
                    ? "No assemblies with the same main profile and length were found in the selected steel."
                    : $"Found {ordered.Count:n0} candidate assembly/assemblies."
            });

            return ordered;
        }

        public void SelectResult(ComparisonResult result)
        {
            if (result?.MainPart == null)
                return;

            var selected = new ArrayList { result.MainPart };
            var selector = new Tekla.Structures.Model.UI.ModelObjectSelector();
            selector.Select(selected);
        }

        private static Part GetSingleSelectedPart()
        {
            var selector = new Tekla.Structures.Model.UI.ModelObjectSelector();
            var selectedObjects = selector.GetSelectedObjects();
            Part selectedPart = null;
            var partCount = 0;

            while (selectedObjects.MoveNext())
            {
                var part = selectedObjects.Current as Part;
                if (part == null)
                    continue;

                selectedPart = part;
                partCount++;

                if (partCount > 1)
                    break;
            }

            if (selectedPart == null)
                throw new InvalidOperationException("Select one part from the assembly you want to find.");

            if (partCount > 1)
                throw new InvalidOperationException("Select only one part when setting the target.");

            return selectedPart;
        }

        private static List<Assembly> GetUniqueSelectedAssemblies()
        {
            var selector = new Tekla.Structures.Model.UI.ModelObjectSelector();
            var selectedObjects = selector.GetSelectedObjects();
            var assemblies = new Dictionary<int, Assembly>();

            while (selectedObjects.MoveNext())
            {
                var selectedAssembly = selectedObjects.Current as Assembly;
                if (selectedAssembly != null)
                {
                    AddAssembly(assemblies, selectedAssembly);
                    continue;
                }

                var part = selectedObjects.Current as Part;
                if (part == null)
                    continue;

                AddAssembly(assemblies, part.GetAssembly());
            }

            return assemblies.Values.ToList();
        }

        private static void AddAssembly(IDictionary<int, Assembly> assemblies, Assembly assembly)
        {
            if (assembly == null)
                return;

            if (!assemblies.ContainsKey(assembly.Identifier.ID))
                assemblies.Add(assembly.Identifier.ID, assembly);
        }

        private static string BuildTargetDescription(AssemblySnapshot target)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Target {0} | {1} | {2:0} mm | {3:0.0} kg | {4} fitting(s)",
                string.IsNullOrWhiteSpace(target.AssemblyMark) ? target.MainPartMark : target.AssemblyMark,
                target.MainProfile,
                target.MainLength,
                target.Weight,
                target.SecondaryParts.Count);
        }

        private static string SafeAssemblyMark(Part mainPart)
        {
            var value = string.Empty;
            mainPart.GetReportProperty("ASSEMBLY_POS", ref value);
            return string.IsNullOrWhiteSpace(value) ? mainPart.Identifier.ID.ToString() : value;
        }
    }
}
