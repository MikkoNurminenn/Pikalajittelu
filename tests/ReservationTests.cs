using System;
using MikkoMods.Storage;

public static class ReservationTests
{
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    public static void Main()
    {
        var request = new ReservationReply(123);
        Check(!request.Granted.HasValue && !request.Received, "Fresh lease already approved");
        Check(!request.Receive(456, true) && !request.Granted.HasValue && !request.Received, "Wrong owner response approved lease");
        Check(request.Receive(123, true) && request.Granted == true && request.Received, "Correct response not accepted");
        Check(!request.Receive(123, false) && request.Granted == true, "Duplicate response changed the decision");
        var denied = new ReservationReply(123);
        Check(denied.Receive(123, false) && denied.Granted == false, "Denied reservation not recorded");
        Check(!denied.Receive(123, true) && denied.Granted == false, "Duplicate granted response overrode denial");
        var expired = new ReservationReply(123) { Expired = true };
        Check(!expired.Receive(456, true) && !expired.Received, "Wrong sender consumed expired reply guard");
        Check(expired.Receive(123, true) && expired.Received && !expired.Granted.HasValue, "Late response approved expired lease");
        Check(!expired.Receive(123, true), "Late duplicate response accepted");
        System.Console.WriteLine("Reservation replies: " + checks + " checks passed (expected owner, denial, expiry, duplicate replies). No transport test implied.");
    }
}
