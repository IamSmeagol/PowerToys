#pragma once

class Trace
{
public:
    static void RegisterProvider();
    static void UnregisterProvider();

    // Log if the user has ZoomIt enabled or disabled
    static void EnableZoomIt(const bool enabled) noexcept;

};