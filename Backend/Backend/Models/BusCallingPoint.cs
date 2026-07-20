namespace Backend.Models
{
    public class BusCallingPoint
    {
        public long Id { get; init; }   

        // Foreign key back to the parent journey — this is the shared key.
        public required string BusTimetableId { get; init; }

        public required int Sequence { get; init; }     
        public required string BusStopId { get; init; }  

        // A stop can have both; intermediate stops often only one.
        public TimeOnly? ArrivalTime { get; init; }
        public TimeOnly? DepartureTime { get; init; }
    }
}
