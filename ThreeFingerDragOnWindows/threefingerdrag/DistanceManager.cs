using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ThreeFingerDragEngine.utils;
using ThreeFingerDragOnWindows.gestures;
using ThreeFingerDragOnWindows.settings;
using ThreeFingerDragOnWindows.utils;

namespace ThreeFingerDragOnWindows.threefingerdrag;

public class DistanceManager {

    // List of contacts that exists but that can't be used for the distance calculation yet.
    // There is a delay of RELEASE_FINGERS_THRESHOLD_MS before they can affect the distance.
    private Dictionary<int, long> _quarantineContacts = new();
    private List<int> _trustedContacts = new();

    /// <summary>
    /// Find the longest distance between two TouchpadContact of same ID.
    /// When new contacts are registered, there is a delay (quarantine) before they can affect the distance.
    /// Returned distance is 0 if the delay between this call and the last one is higher than RELEASE_FINGERS_THRESHOLD_MS (if hasFingersReleased is true).
    /// </summary>
    /// <param name="oldContacts">First contacts list</param>
    /// <param name="newContacts">Second contacts list</param>
    /// <param name="hasFingersReleased">Whether if fingers has been released and replaced on the touchpad</param>
    /// <returns>(ID of the TouchpadContact, Point distance (in x and y), 2D distance)</returns>
    public (int, Point, float) GetLongestDist2D(TouchpadContact[] oldContacts, TouchpadContact[] newContacts, bool hasFingersReleased){
        if(hasFingersReleased){
            _quarantineContacts.Clear();
            _trustedContacts.Clear();
            return (0, new Point(0, 0), 0);
        }

        float longestDist2D = 0;
        int longestDistId = 0;
        Point longestDistPoint = Point.Empty;

        // Quarantine system

        // Remove contacts that don't exist anymore
        _trustedContacts.RemoveAll(c => newContacts.All(nc => nc.ContactId != c));
        _quarantineContacts = _quarantineContacts.Where(c => newContacts.Any(nc => nc.ContactId == c.Key))
            .ToDictionary(c => c.Key, c => c.Value);

        // Add new contacts
        foreach(var newContact in newContacts){
            long contactCtms;
            if(_quarantineContacts.TryGetValue(newContact.ContactId, out contactCtms)){
                // Contact exist in quarantine
                if(Ctms() - contactCtms > ThreeFingerDrag.RELEASE_FINGERS_THRESHOLD_MS){
                    // Contact is now trusted
                    _trustedContacts.Add(newContact.ContactId);
                    _quarantineContacts.Remove(newContact.ContactId);
                }
            } else{
                // Contact is new
                _quarantineContacts.Add(newContact.ContactId, Ctms());
            }
        }

        // Checking the longest distance

        foreach(var newContact in newContacts){
            // If the contact is not trusted, skip it
            if(!_trustedContacts.Contains(newContact.ContactId)) continue;
            
            foreach(var oldContact in oldContacts){
                if(newContact.ContactId != oldContact.ContactId) continue;

                float dist2D = newContact.GetDist2D(oldContact);
                if(dist2D > longestDist2D){
                    longestDist2D = dist2D;
                    longestDistId = newContact.ContactId;
                    longestDistPoint = new Point(newContact.X - oldContact.X, newContact.Y - oldContact.Y);
                }

                break;
            }
        }

        return (longestDistId, longestDistPoint, longestDist2D);
    }


    public static Point ApplySpeedAndAcc(
        IntPtr currentDevice,
        Point delta,
        int elapsed,
        double velocityDistance,
        PointerVelocityFilter velocityFilter){

        // Apply Speed
        var deviceInfo = TouchpadHelper.GetDeivceInfo(currentDevice);
        var deviceId = deviceInfo?.deviceId ?? "default";
        var config = App.SettingsData.ThreeFingerDeviceDragCursorConfigs.GetValueOrDefault(deviceId, new SettingsData.ThreeFingerDragConfig());
        var speedScale = config.ThreeFingerDragCursorSpeed / 120;
        delta.Multiply(speedScale);

        // Use a short time-based history rather than a single report. Very short
        // scheduling bursts are normalized by the filter's report-period floor.
        var sampledDistance = Math.Max(0, velocityDistance) * speedScale;
        var mouseVelocity = Math.Min(velocityFilter.Update(sampledDistance, elapsed), 4);
        if(double.IsNaN(mouseVelocity) || double.IsInfinity(mouseVelocity)) mouseVelocity = 1;


        // Missing device preferences mean the same defaults used by the UI,
        // not a separate unaccelerated mode that changes after opening settings.
        var pointerVelocity = DragPointerGain.Calculate(mouseVelocity, config.ThreeFingerDragCursorAcceleration);
        if(config.ThreeFingerDragCursorAcceleration != 0)
            Logger.Log("    pointerVelocity: " + pointerVelocity + " (mouseVelocity: " + mouseVelocity + ")");

        // Apply acceleration
        delta.Multiply(pointerVelocity);

        return delta;
    }

    public static float ApplySpeed(IntPtr currentDevice, float distance){
        var deviceInfo = TouchpadHelper.GetDeivceInfo(currentDevice);
        distance *= App.SettingsData.ThreeFingerDeviceDragCursorConfigs.GetValueOrDefault(deviceInfo.deviceId, new SettingsData.ThreeFingerDragConfig()).ThreeFingerDragCursorSpeed / 60;
        return distance;
    }

    private long Ctms(){
        return new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds();
    }
    
}
