namespace Tweaks.Items.Hex
{
    internal static class HexOfOssification
    {
        internal static void Init()=>HexConfigSupport.Register("BarrierEnemiesOnKill",Configure);
        static void Configure(HexConfig c)
        {
            const string type="RoR2.GlobalEventManager",method="ProcBarrierEnemiesOnKill";
            c.Number("Radius",30,"Base barrier distribution radius in metres.");
            c.Number("Radius Per Stack",15,"Extra radius per additional item.");
            c.Values.Literal(type,method,15,()=>c.Values.F("Radius")-c.Values.F("Radius Per Stack"),occurrence:0);
            c.Values.Literal(type,method,15,()=>c.Values.F("Radius Per Stack"),occurrence:1);
            c.Literal(type,method,1,"Victim Health Granted Per Stack (%)",100,0);
            c.Literal(type,method,.01f,"Minimum Target Health Granted Per Stack (%)",100);
            c.Literal(type,method,.005f,"Large Boss Minimum Health Coefficient (%)",100);
        }
    }
}
