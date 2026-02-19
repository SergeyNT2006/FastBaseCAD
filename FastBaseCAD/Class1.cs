using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System;

public class Commands
{
    [CommandMethod("FASTBASE")]
    public void FastBaseProjectionV11()
    {
        Document doc = Application.DocumentManager.MdiActiveDocument;
        Database db = doc.Database;
        Editor ed = doc.Editor;

        try
        {
            // 1. БОКОВАЯ СТОРОНА AB (2 точки)
            PromptPointOptions ppo1 = new PromptPointOptions("\nPoint A (start of side): ");
            PromptPointResult ptA = ed.GetPoint(ppo1);
            if (ptA.Status != PromptStatus.OK) return;

            PromptPointOptions ppo2 = new PromptPointOptions("\nPoint B (end of side): ");
            ppo2.BasePoint = ptA.Value;
            PromptPointResult ptB = ed.GetPoint(ppo2);
            if (ptB.Status != PromptStatus.OK) return;

            Point3d A = ptA.Value;
            Point3d B = ptB.Value;

            // 2. ОСНОВАНИЕ - ЛИНИЯ (2 точки)
            PromptPointOptions ppo3 = new PromptPointOptions("\nBase line point 1: ");
            PromptPointResult ptC = ed.GetPoint(ppo3);
            if (ptC.Status != PromptStatus.OK) return;

            PromptPointOptions ppo4 = new PromptPointOptions("\nBase line point 2: ");
            ppo4.BasePoint = ptC.Value;
            PromptPointResult ptD = ed.GetPoint(ppo4);
            if (ptD.Status != PromptStatus.OK) return;

            Point3d C = ptC.Value;
            Point3d D = ptD.Value;

            // 3. ГЕОМЕТРИЯ
            Line baseLine = new Line(C, D);

            // 🔴 ВЫСОТА h = проекция точки B на CD
            Point3d projB = baseLine.GetClosestPointTo(B, false);
            double h = B.DistanceTo(projB);

            // 🟢 ПРОЕКЦИЯ ОТРЕЗКА AB на CD
            Point3d projA = baseLine.GetClosestPointTo(A, false);
            double p = projA.DistanceTo(projB);

            // 4. РИСУЕМ (Transaction)
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // 🔴 КРАСНАЯ: высота B→projB
                Line heightLine = new Line(B, projB);
                heightLine.ColorIndex = 1; // Красный
                btr.AppendEntity(heightLine);
                tr.AddNewlyCreatedDBObject(heightLine, true);

                // 🟢 ЗЕЛЁНАЯ: проекция AB на CD
                Line projLineAB = new Line(projA, projB);
                projLineAB.ColorIndex = 3; // Зеленый
                projLineAB.LineWeight = LineWeight.LineWeight050;
                btr.AppendEntity(projLineAB);
                tr.AddNewlyCreatedDBObject(projLineAB, true);

                // 📏 СЕРЕДИНА красной линии (ПРАВИЛЬНО!)
                Vector3d vecHeight = (projB - B) * 0.5;
                Point3d midHeight = B + vecHeight;
                DBText textH = new DBText();
                textH.Position = midHeight + new Vector3d(3, 0, 0);
                textH.Height = 2.5;
                textH.TextString = $"h = {h:F1} mm";
                textH.ColorIndex = 1; // Красный
                btr.AppendEntity(textH);
                tr.AddNewlyCreatedDBObject(textH, true);

                // 📏 СЕРЕДИНА зеленой линии (ПРАВИЛЬНО!)
                Vector3d vecProj = (projB - projA) * 0.5;
                Point3d midProj = projA + vecProj;
                DBText textP = new DBText();
                textP.Position = midProj + new Vector3d(0, 3, 0);
                textP.Height = 2.5;
                textP.TextString = $"p = {p:F1} mm";
                textP.ColorIndex = 3; // Зеленый
                btr.AppendEntity(textP);
                tr.AddNewlyCreatedDBObject(textP, true);

                tr.Commit();
            }

            // ВЫВОД РЕЗУЛЬТАТОВ
            ed.WriteMessage("\n🚀 ПРОЕКЦИИ ТРАПЕЦИИ СОЗДАНЫ v1.1!");
            ed.WriteMessage($"   🔴 Высота h = {h:F1} mm");
            ed.WriteMessage($"   🟢 Проекция p = {p:F1} mm");
            ed.WriteMessage($"   📐 Длина боковой AB = {A.DistanceTo(B):F1} mm");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\nОшибка: " + ex.Message);
        }
    }
}