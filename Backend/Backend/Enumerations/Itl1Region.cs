namespace Backend.Enumerations
{
    // The twelve ITL level 1 regions of the UK: nine English regions plus each of the other three nations. The names
    // match the ITL121CD codes carried in ITL1UK.geojson, which is what the boundaries are looked up by.
    public enum Itl1Region
    {
        // A coordinate no boundary contains, which in practice means a stop just off the coastline.
        None = 0,

        NorthEast,
        NorthWest,
        YorkshireAndTheHumber,
        EastMidlands,
        WestMidlands,
        EastOfEngland,
        London,
        SouthEast,
        SouthWest,
        Wales,
        Scotland,
        NorthernIreland
    }
}
