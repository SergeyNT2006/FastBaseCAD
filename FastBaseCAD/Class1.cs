using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System;

public class Commands
{
    private static class Config
    {
        public static double MIN_LENGTH = 0.1;
        public static double TEXT_OFFSET_X = 3.0;
        public static double TEXT_OFFSET_Y = 3.0;
        public static LineWeight PROJ_LINEWEIGHT = LineWeight.LineWeight100;
        public static int RED_COLOR_INDEX = 1;
        public static int GREEN_COLOR_INDEX = 3;
        public static double TEXT_HEIGHT = 2.5;
    }

    private static string _lastMode = "Points";

    [CommandMethod("FB")]
    public void FastBaseProjectionV21()
    {
        Document doc = Application.DocumentManager.MdiActiveDocument;
        Database db = doc.Database;
        Editor ed = doc.Editor;

        try
        {
            // ============================================================================
            // STEP 1: CREATE REQUIRED LAYERS (HEIGHTS, PROJECTIONS)
            // ============================================================================
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
                        SafeWriteMessage(ed, $"\nCreated layer: {layerName}");
                    }
                }
                trLayers.Commit();
            }

            // ============================================================================
            // STEP 2: SELECT OPERATING MODE (Enter = last mode)
            // ============================================================================
            PromptStringOptions pso = new PromptStringOptions($"\nMode: [P]oints/[L]ines/[S]tyle <{_lastMode}> [Enter=last]: ");
            pso.AllowSpaces = false;
            PromptResult prMode = ed.GetString(pso);

            bool pointMode = true;
            string userInput = prMode.StringResult?.ToUpper() ?? "";

            
            if (prMode.Status == PromptStatus.Cancel || prMode.Status == PromptStatus.Error)
            {
                return;
            }
            else if (userInput == "S")
            {
                CreateTextStylesSafe(db, ed);
                return;
            }
            else if (string.IsNullOrEmpty(userInput))
            {
                pointMode = _lastMode == "Points";
                SafeWriteMessage(ed, $"\nUsing last mode: {_lastMode}");
            }
            else if (userInput == "P" || userInput == "POINTS")
            {
                pointMode = true;
                _lastMode = "Points";
                SafeWriteMessage(ed, "\nPoints mode selected");
            }
            else if (userInput == "L" || userInput == "LINES")
            {
                pointMode = false;
                _lastMode = "Lines";
                SafeWriteMessage(ed, "\nLines mode selected");
            }
            else
            {
                SafeWriteMessage(ed, "\nInvalid input. Using last mode.");
                pointMode = _lastMode == "Points";
            }

            // ============================================================================
            // STEP 3: COLLECT GEOMETRY DATA (A, B, C, D points)
            // ============================================================================
            Point3d A, B, C, D;
            ObjectId sideObjectId = ObjectId.Null;

            if (pointMode)
            {
                PromptPointOptions ppo1 = new PromptPointOptions("\nSide start: ");
                PromptPointResult ptA = ed.GetPoint(ppo1);
                if (ptA.Status != PromptStatus.OK) return;

                PromptPointOptions ppo2 = new PromptPointOptions("\nStart end: ");
                ppo2.BasePoint = ptA.Value;
                PromptPointResult ptB = ed.GetPoint(ppo2);
                if (ptB.Status != PromptStatus.OK) return;

                PromptPointOptions ppo3 = new PromptPointOptions("\nBase 1: ");
                PromptPointResult ptC = ed.GetPoint(ppo3);
                if (ptC.Status != PromptStatus.OK) return;

                PromptPointOptions ppo4 = new PromptPointOptions("\nBase 2: ");
                ppo4.BasePoint = ptC.Value;
                PromptPointResult ptD = ed.GetPoint(ppo4);
                if (ptD.Status != PromptStatus.OK) return;

                A = ptA.Value;
                B = ptB.Value;
                C = ptC.Value;
                D = ptD.Value;
            }
            else
            {
                PromptEntityOptions peo1 = new PromptEntityOptions("\nSelect side line/polyline segment: ");
                peo1.SetRejectMessage("\nPlease select Line or Polyline only");
                PromptEntityResult per1 = ed.GetEntity(peo1);
                if (per1.Status != PromptStatus.OK) return;

                sideObjectId = per1.ObjectId;
                HighlightEntity(db, sideObjectId, true);

                PromptEntityOptions peo2 = new PromptEntityOptions("\nSelect base line/polyline segment: ");
                peo2.SetRejectMessage("\nPlease select Line or Polyline only");
                PromptEntityResult per2 = ed.GetEntity(peo2);
                if (per2.Status != PromptStatus.OK)
                {
                    HighlightEntity(db, sideObjectId, false);
                    return;
                }

                using (Transaction trEnt = db.TransactionManager.StartTransaction())
                {
                    Entity sideEnt = (Entity)trEnt.GetObject(per1.ObjectId, OpenMode.ForRead);
                    Entity baseEnt = (Entity)trEnt.GetObject(per2.ObjectId, OpenMode.ForRead);

                    if (sideEnt is Line sideLine)
                    {
                        A = sideLine.StartPoint;
                        B = sideLine.EndPoint;
                    }
                    else if (sideEnt is Polyline sidePoly)
                    {
                        Point3d pickPt = per1.PickedPoint;
                        int closestVertex = FindClosestSegment(sidePoly, pickPt);
                        A = sidePoly.GetPoint3dAt(closestVertex);
                        B = sidePoly.GetPoint3dAt((closestVertex + 1) % sidePoly.NumberOfVertices);
                    }
                    else
                    {
                        SafeWriteMessage(ed, "\nUnsupported entity type for side");
                        HighlightEntity(db, sideObjectId, false);
                        return;
                    }

                    if (baseEnt is Line baseLineObj)
                    {
                        C = baseLineObj.StartPoint;
                        D = baseLineObj.EndPoint;
                    }
                    else if (baseEnt is Polyline basePoly)
                    {
                        Point3d basePickPt = per2.PickedPoint;
                        int baseClosestVertex = FindClosestSegment(basePoly, basePickPt);
                        C = basePoly.GetPoint3dAt(baseClosestVertex);
                        D = basePoly.GetPoint3dAt((baseClosestVertex + 1) % basePoly.NumberOfVertices);
                    }
                    else
                    {
                        SafeWriteMessage(ed, "\nUnsupported entity type for base");
                        HighlightEntity(db, sideObjectId, false);
                        return;
                    }

                    trEnt.Commit();
                }

                HighlightEntity(db, sideObjectId, false);
            }


            // ============================================================================
            // VALIDATION: Check minimum lengths
            // ============================================================================
            double abLength = A.DistanceTo(B);
            double cdLength = C.DistanceTo(D);

            if (abLength < Config.MIN_LENGTH)
            {
                SafeWriteMessage(ed, "\n⚠️ Side length too small (minimum 0.1mm)");
                return;
            }
            if (cdLength < Config.MIN_LENGTH)
            {
                SafeWriteMessage(ed, "\n⚠️ Base line length too small (minimum 0.1mm)");
                return;
            }

            // ============================================================================
            // STEP 4: GEOMETRY CALCULATIONS
            // ============================================================================

            Line baseLineGeo = new Line(C, D);
            Point3d projB = baseLineGeo.GetClosestPointTo(B, false);
            double h = B.DistanceTo(projB);
            Point3d projA = baseLineGeo.GetClosestPointTo(A, false);
            double p = projA.DistanceTo(projB);

            if (h < Config.MIN_LENGTH || p < Config.MIN_LENGTH)
            {
                SafeWriteMessage(ed, "\n⚠️ Cannot build projection/height (zero values detected)");
                return;
            }

            // ============================================================================
            // STEP 5: DRAW RESULTS - PERFECT TEXT POSITIONING + TEXT STYLES
            // ============================================================================
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // 🔴 RED HEIGHT LINE (B → projB)
                Line heightLine = new Line(B, projB);
                heightLine.ColorIndex = Config.RED_COLOR_INDEX;
                heightLine.Layer = "HEIGHTS";
                btr.AppendEntity(heightLine);
                tr.AddNewlyCreatedDBObject(heightLine, true);

                // 🟢 GREEN PROJECTION LINE (projA → projB)
                Line projLineAB = new Line(projA, projB);
                projLineAB.ColorIndex = Config.GREEN_COLOR_INDEX;
                projLineAB.LineWeight = Config.PROJ_LINEWEIGHT;
                projLineAB.Layer = "PROJECTIONS";
                btr.AppendEntity(projLineAB);
                tr.AddNewlyCreatedDBObject(projLineAB, true);

                // ✅ Applyed Style to text (safety)
                ObjectId styleHeightId = ObjectId.Null;
                ObjectId styleProjId = ObjectId.Null;
                TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                if (tst.Has("FB_hight")) styleHeightId = tst["FB_hight"];
                if (tst.Has("FB_base")) styleProjId = tst["FB_base"];

                // 📏 HEIGHT TEXT - IDEAL POSITIONING (perpendicular to height line)
                Vector3d heightDir = (projB - B).GetNormal();
                Vector3d heightPerp = Vector3d.ZAxis.CrossProduct(heightDir) * Config.TEXT_OFFSET_X;
                Point3d midHeight = B + (projB - B) * 0.5;
                DBText textH = new DBText();
                textH.Position = midHeight + heightPerp;
                textH.TextStyleId = styleHeightId;  // ✅ Use FB_hight text style
                textH.TextString = $"h = {h:F1} mm";
                textH.ColorIndex = Config.RED_COLOR_INDEX;
                textH.Layer = "HEIGHTS";
                btr.AppendEntity(textH);
                tr.AddNewlyCreatedDBObject(textH, true);

                // 📏 PROJECTION TEXT - IDEAL POSITIONING (perpendicular to projection line)
                Vector3d projDir = (projB - projA).GetNormal();
                Vector3d projPerp = Vector3d.ZAxis.CrossProduct(projDir) * Config.TEXT_OFFSET_Y;
                Point3d midProj = projA + (projB - projA) * 0.5;
                DBText textP = new DBText();
                textP.Position = midProj + projPerp;
                textP.TextStyleId = styleProjId;    // ✅ Use FB_base style
                textP.TextString = $"p = {p:F1} mm";
                textP.ColorIndex = Config.GREEN_COLOR_INDEX;
                textP.Layer = "PROJECTIONS";
                btr.AppendEntity(textP);
                tr.AddNewlyCreatedDBObject(textP, true);

                tr.Commit();
            }


            // ============================================================================
            // SUCCESS OUTPUT with detailed measurements
            // ============================================================================
            SafeWriteMessage(ed, "\n🚀 FastBase v2.1 - Projections created successfully!");
            SafeWriteMessage(ed, $"\n   {_lastMode} mode:");
            SafeWriteMessage(ed, $"\n   Side = {abLength:F1} mm");
            SafeWriteMessage(ed, $"\n   Base = {cdLength:F1} mm");
            SafeWriteMessage(ed, $"\n   h = {h:F1} mm");
            SafeWriteMessage(ed, $"\n   p = {p:F1} mm");
        }
        catch (System.Exception ex)
        {
            SafeWriteMessage(ed, $"\n❌ Error: {ex.Message}");
        }
    }

    // ============================================================================
    // UTILITY FUNCTIONS
    // ============================================================================

    /// <summary>
    /// Creates FastBase text styles "FB_hight" and "FB_base" if they don't exist.
    /// Uses Romans.shx font with configurable text height (Config.TEXT_HEIGHT).
    /// Safely checks for existing styles before creation and provides user feedback.
    /// Handles all exceptions silently to prevent command crashes.
    /// </summary>
    

    private void CreateTextStylesSafe(Database db, Editor ed)
    {
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            try
            {
                TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);

                if (tst.Has("FB_hight") && tst.Has("FB_base"))
                {
                    SafeWriteMessage(ed, "\n✅ FB text styles ALREADY EXIST: FB_hight, FB_base");
                    return;
                }

                TextStyleTable tstWrite = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForWrite);

                if (!tst.Has("FB_hight"))
                {
                    TextStyleTableRecord tstrH = new TextStyleTableRecord();
                    tstrH.Name = "FB_hight";
                    tstrH.FileName = "romans.shx";
                    tstrH.TextSize = Config.TEXT_HEIGHT;
                    tstWrite.Add(tstrH);
                    tr.AddNewlyCreatedDBObject(tstrH, true);
                }

                if (!tst.Has("FB_base"))
                {
                    TextStyleTableRecord tstrB = new TextStyleTableRecord();
                    tstrB.Name = "FB_base";
                    tstrB.FileName = "romans.shx";
                    tstrB.TextSize = Config.TEXT_HEIGHT;
                    tstWrite.Add(tstrB);
                    tr.AddNewlyCreatedDBObject(tstrB, true);
                }

                tr.Commit();
                SafeWriteMessage(ed, "\n✅ FB text styles CREATED: FB_hight, FB_base");
            }
            catch (System.Exception)
            {
                SafeWriteMessage(ed, "\n❌ Error creating styles");
            }
        }
    }

    /// <summary>
    /// Safely writes message to editor (prevents crashes from formatting issues)
    /// </summary>
    private void SafeWriteMessage(Editor ed, string message)
    {
        try
        {
            ed.WriteMessage(message);
        }
        catch
        {
            // Silent fail - message logging is not critical
        }
    }
    /// <summary>
    /// Highlights or unhighlights an entity (with smooth screen update)
    /// </summary>
    private void HighlightEntity(Database db, ObjectId entityId, bool highlight)
    {
        if (entityId.IsNull) return;

        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            Entity ent = (Entity)tr.GetObject(entityId, OpenMode.ForWrite);
            if (highlight)
                ent.Highlight();
            else
                ent.Unhighlight();
            tr.Commit();
        }

        Application.DocumentManager.MdiActiveDocument.Editor.Regen();
    }

    /// <summary>
    /// Finds closest polyline segment to pick point (for precise segment selection)
    /// </summary>
    private int FindClosestSegment(Polyline poly, Point3d pickPt)
    {
        double minDist = double.MaxValue;
        int closestVertex = 0;

        for (int i = 0; i < poly.NumberOfVertices - 1; i++)
        {
            Point3d v1 = poly.GetPoint3dAt(i);
            Point3d v2 = poly.GetPoint3dAt(i + 1);
            Point3d closestOnSeg = GetClosestPointOnSegment(pickPt, v1, v2);
            double dist = pickPt.DistanceTo(closestOnSeg);

            if (dist < minDist)
            {
                minDist = dist;
                closestVertex = i;
            }
        }
        return closestVertex;
    }

    /// <summary>
    /// Calculates closest point on finite line segment (bounded between endpoints)
    /// </summary>
    private Point3d GetClosestPointOnSegment(Point3d point, Point3d segStart, Point3d segEnd)
    {
        Line segLine = new Line(segStart, segEnd);
        return segLine.GetClosestPointTo(point, false);
    }
}