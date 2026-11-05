using System;

/// <summary>
/// A procedure as described in a JSON file in Assets/_Project/Data/Procedures/.
/// Field names must match the JSON keys exactly (Unity's JsonUtility).
/// </summary>
[Serializable]
public class Procedure
{
    public string id;               // e.g. "battery_change"
    public string title;            // shown in the menu, e.g. "Change the battery"
    public ProcedureStep[] steps;
}

[Serializable]
public class ProcedureStep
{
    public string text;             // one line for the step card
    public string target;           // Model Target key, e.g. "FaceUp" / "FaceDown"
    public string[] highlight;      // CAD part names exactly as in Fusion, e.g. "BatteryLid"
    public string[] outline;        // parts that get an orange ring around them instead, e.g. "UnitButton"
    public string[] pointAt;        // marker names, e.g. "Display_Temperature"
    public string[] hide;           // parts that are already removed in this step, e.g. "BatteryLid"
    public string animation;        // name of a StepAnimation, or empty
    public bool xray;               // show the battery compartment in x-ray mode
}
