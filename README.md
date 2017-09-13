# NMEA Parser for Serial COM Ports in ESRI's Runtime API
Using ESRI's Runtime 100.x framework, this class accesses a Serial COM port and provides information based on the NMEA strings read from the port

Class for converting NMEA GPS data supplied through a Serial COM Port into a format that is usable by ESRI's runtime library.
Notes about usage: When calling the StopAsync function, either dierctly or through MapView.LocationDispaly.IsEnabled, you must either set up an event handler to listen for the HasClosed event in this class or else Await the Task associated with the StopAsync Function.  Until either of these occurs, the user cannot be allowed to restart the application or they can lock the application.  This is due to the resources that the serial port is using being on another thread, so the application needs to wait for that other thread to complete to be sure all the resources have been released and it is safe to reconnect to the com port.
You must also wait for the HasClosed event to fire or the Task from OnStop to complete when closing the form that originally owned the  instance of the class.  If you don't, the form will not be able to close as it will still be receiving updates from the class, which will block the form from closing.

Before any call to start the class, the COM Port and Baud Rate must be set through the appropriate properties for the class.

It should be noted that in testing, using the Event path worked better with ESRI's MapView.LocationDisplay functionality.  Directly calling the StopAsync doesn't properly disconnect the data supply from the map, so even though you have called it, locations still are displayed on the map.  Similarly, on start up, the positions will display, but the MapView.LocationDisplay will not honour the defined AutoPanMode setting.
