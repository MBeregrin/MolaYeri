namespace WeBussedUp.NPC
{
    public enum VehicleType    { Car, Bus, Truck }
    public enum CustomerGender { Male, Female, Child }
    public enum CustomerNeed   { None, Shopping, Cafe, Fuel, CarWash, Restroom }
    public enum CustomerState  { SpawningFromVehicle, MovingToStation, WaitingInQueue, BeingServed, MovingToExit, EnteringVehicle }
    public enum VehicleState   { OnHighway, EnteringLot, Parking, Parked, Exiting, Despawning }
}