using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System;

public class Commands
{
    [CommandMethod("FB")]
    public void FastBaseProjectionV11()
    {
        Document doc = Application.DocumentManager.MdiActiveDocument;
        Database db = doc.Database;
        Editor ed = doc.Editor;

        try
        {
            // Создание слоев HEIGHTS и PROJECTIONS
            using (Transaction trLayers = db.TransactionManager.StartTransaction())
            {
                LayerTable lt = (LayerTable)trLayers.GetObject(db.LayerTableId, OpenMode.ForWrite);

                string[] layerNames = { "HEIGHTS", "PROJECTIONS" };
                foreach (string layerName in layerNames)
                {
                    if (!lt.Has(layerName))
                    {
                        LayerTableRecord ltr = new LayerTableRecord();
                        ltr.Name = layerName;
                        lt.Add(ltr);
                        trLayers.AddNewlyCreatedDBObject(ltr, true);
                        ed.WriteMessage($"\nCreated layer: {layerName}");
                    }
                }
                trLayers.Commit();
            }

            // 1. SIDE AB (2 points)
            PromptPointOptions ppo1 = new PromptPointOptions("\\nPoint A (start of side): ");
            PromptPointResult ptA = ed.GetPoint(ppo1);
            if (ptA.Status != PromptStatus.OK) return;

            PromptPointOptions ppo2 = new PromptPointOptions("\\nPoint B (end of side): ");
            ppo2.BasePoint = ptA.Value;
            PromptPointResult ptB = ed.GetPoint(ppo2);
            if (ptB.Status != PromptStatus.OK) return;

            Point3d A = ptA.Value;
            Point3d B = ptB.Value;

            // 2. BASE LINE (2 points)
            PromptPointOptions ppo3 = new PromptPointOptions("\\nBase line point 1: ");
            PromptPointResult ptC = ed.GetPoint(ppo3);
            if (ptC.Status != PromptStatus.OK) return;

            PromptPointOptions ppo4 = new PromptPointOptions("\\nBase line point 2: ");
            ppo4.BasePoint = ptC.Value;
            PromptPointResult ptD = ed.GetPoint(ppo4);
            if (ptD.Status != PromptStatus.OK) return;

            Point3d C = ptC.Value;
            Point3d D = ptD.Value;

            // 3. GEOMETRY
            Line baseLine = new Line(C, D);

            // 🔴 HEIGHT h = projection of point B onto CD
            Point3d projB = baseLine.GetClosestPointTo(B, false);
            double h = B.DistanceTo(projB);

            // 🟢 PROJECTION OF SEGMENT AB onto CD
            Point3d projA = baseLine.GetClosestPointTo(A, false);
            double p = projA.DistanceTo(projB);

            // ✅ ЕДИНАЯ ПРОВЕРКА: если h или p = 0
            if (h < 0.1 || p < 0.1)
            {
                ed.WriteMessage("\\n⚠️  Cannot build projection/height (zero values detected)");
                return; // Ничего не строим
            }

            // 4. DRAW (Transaction)
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // 🔴 RED: height B→projB (layer HEIGHTS)
                Line heightLine = new Line(B, projB);
                heightLine.ColorIndex = 1; // Red
                heightLine.Layer = "HEIGHTS";
                btr.AppendEntity(heightLine);
                tr.AddNewlyCreatedDBObject(heightLine, true);

                // 🟢 GREEN: projection AB onto CD (layer PROJECTIONS)
                Line projLineAB = new Line(projA, projB);
                projLineAB.ColorIndex = 3; // Green
                projLineAB.LineWeight = LineWeight.LineWeight050;
                projLineAB.Layer = "PROJECTIONS";
                btr.AppendEntity(projLineAB);
                tr.AddNewlyCreatedDBObject(projLineAB, true);

                // 📏 HEIGHT TEXT (layer HEIGHTS)
                Vector3d vecHeight = (projB - B) * 0.5;
                Point3d midHeight = B + vecHeight;
                DBText textH = new DBText();
                textH.Position = midHeight + new Vector3d(3, 0, 0);
                textH.Height = 2.5;
                textH.TextString = $"h = {h:F1} mm";
                textH.ColorIndex = 1; // Red
                textH.Layer = "HEIGHTS";
                btr.AppendEntity(textH);
                tr.AddNewlyCreatedDBObject(textH, true);

                // 📏 PROJECTION TEXT (layer PROJECTIONS)
                Vector3d vecProj = (projB - projA) * 0.5;
                Point3d midProj = projA + vecProj;
                DBText textP = new DBText();
                textP.Position = midProj + new Vector3d(0, 3, 0);
                textP.Height = 2.5;
                textP.TextString = $"p = {p:F1} mm";
                textP.ColorIndex = 3; // Green
                textP.Layer = "PROJECTIONS";
                btr.AppendEntity(textP);
                tr.AddNewlyCreatedDBObject(textP, true);

                tr.Commit();
            }

            // OUTPUT RESULTS
            ed.WriteMessage("\\n🚀 Projections created v1.3!");
            ed.WriteMessage($"   🔴 Height h = {h:F1} mm (layer HEIGHTS)");
            ed.WriteMessage($"   🟢 Projection p = {p:F1} mm (layer PROJECTIONS)");
            ed.WriteMessage($"   📐 Side AB length = {A.DistanceTo(B):F1} mm");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\\nError: " + ex.Message);
        }
    }
}