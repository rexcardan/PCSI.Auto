using ESAPIX.Common;
using ESAPIX.Common.Args;
using ESAPIX.Extensions;
using System;
using System.Collections.Generic;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using V = VMS.TPS.Common.Model.API;
using PCSI = ESAPIX.Helpers.Autoplanning.ProtonCSIBuilder;
using static ESAPIX.Helpers.Strings.MagicStrings;
using System.Linq;
using ESAPIX.Helpers.Strings;

[assembly: V.ESAPIScript(IsWriteable = true)]

namespace ESAPIScriptTemplate
{
    class Program
    {
        const string COUCH_ID = "ProbeamTable"; //Needs to be yours
        const string PROTON_MACHINE = "Probeam"; //Needs to be yours
        const string TOLERANCE_TABLE = "PROTONS"; //Needs to be yours
        const string SNOUT_ID = "S1"; //Needs to be yours
        const string RANGE_SHIFTER = "RS57mm"; //Needs to be yours

        static ConsoleUI _ui = new ConsoleUI();
        static bool _requestExit = false;

        [STAThread]
        static void Main(string[] args)
        {
            _ui.WriteSectionHeader("Scripting CSI for Protons");
            var context = new StandAloneContext(Application.CreateApplication());
            if (args != null) { ArgContextSetter.Set(context, args); }
            
            //Let user pick patient
            _ui.GetPatient(context);

            //Optional enable write
            context.Patient.BeginModifications();
            //Optional - manual set focus
            var course = _ui.GetCourse(context);
            var ss = _ui.GetStructureSet(context);

            while (!_requestExit)
            {
                var options = new Dictionary<string, Action>()
                {
                   {"Create Control Structures", ()=>{ PCSI.CreateStructures(ss); context.Application.SaveModifications(); } },
                   {"Remove Control Structures", ()=>{ss.RemoveAll("^_otv.*"); ss.RemoveAll("^_ctv.*"); context.Application.SaveModifications(); } },
                   {"Add Wedge Uncertainty", ()=>{ AddWedgeUncertainty(context); context.Application.SaveModifications(); } },
                   {"Create CSI Plan", ()=>{CreatePlan(context); context.Application.SaveModifications(); } },
                   {"Exit", ()=> _requestExit = true },
                };

                _ui.GetResponseAndDoAction(options);
            }

            context.Application.Dispose();
        }

        private static void CreatePlan(StandAloneContext context)
        {
            var ion = context.Course.AddOrResetIonPlan(context.StructureSet, COUCH_ID, _ui.GetStringInput("What is the plan name to create?"));
            ion.SetPrescription(20, DoseParser.Parse("180 cGy"), 1);
            ion.SetOptimizationMode(IonPlanOptimizationMode.MultiFieldOptimization); // MFO
            PCSI.AddOptimizationGoals(ion);
            var mParams = new ProtonBeamMachineParameters(PROTON_MACHINE, MagicStrings.Technique.MODULATED_SCANNING, TOLERANCE_TABLE);
            PCSI.AddBeams(ion, mParams, SNOUT_ID, RANGE_SHIFTER);
            context.PlanSetup = ion;
            context.IonPlanSetup = ion;
        }

        private static void AddWedgeUncertainty(StandAloneContext context)
        {
            var wedge = context.StructureSet.Find(string.Empty, "^__CShell.*");

            if (wedge != null)
            {
                var supMargin = new AxisAlignedMargins(StructureMarginGeometry.Outer, 0, 3, 0, 0, 0, 0);
                var infMargin = new AxisAlignedMargins(StructureMarginGeometry.Outer, 0, 0, 0, 0, 3, 0);
                var tip = wedge.MeshGeometry.Bounds.Z + wedge.MeshGeometry.Bounds.SizeZ;
                var controlOTVs = context.StructureSet.FindAll(string.Empty, "^_otv.*")
                    .Where(o =>
                    {
                        var bounds = o.MeshGeometry.Bounds;
                        return bounds.Z < tip && (bounds.Z + bounds.SizeZ) > tip;
                    }).ToList();

                foreach (var otv in controlOTVs)
                {
                    otv.AsymmetricMarginInBounds(supMargin, context.StructureSet, (tip, tip + 10));
                    otv.AsymmetricMarginInBounds(infMargin, context.StructureSet, (tip - 10, tip));
                }
            }
        }
    }
}
