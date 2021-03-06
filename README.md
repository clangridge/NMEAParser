# NMEA Parser for Serial COM Ports in ESRI's Runtime API
Using ESRI's Runtime 100.x framework, this class accesses a Serial COM port and provides information based on the NMEA strings read from the port

Class for converting NMEA GPS data supplied through a Serial COM Port into a format that is usable by ESRI's runtime library.
Notes about usage: The class's OnStartAsync function will check to see if a previous close requests have been completed, and if not it will wait until the closing operation has been completed before attempting to open a connection to the COM port again.  

While this works well with user requests to stop and start the GPS.  It fails, however, when trying to stop the GPS as part of the FormClosing event because, as it pointed out in the StackOverFlow question https://stackoverflow.com/questions/16656523/awaiting-asynchronous-function-inside-formclosing-event#18200127, having await tasks happen when the form is trying to close rarely ends well.  The cleanest way we have found is:
1) check if the GPS is running; 
2) if yes, cancel the closing event;
3) await the StopAsync function to complete;
4) Call the Close Sub again; and
5) exit the current Close event handler

Failure to do this will result in either the form not closing as it continues to try and handle the data coming from the COM port or else threading errors as incomplete threads try to terminate.

Before any call to start the class, the COM Port and Baud Rate must be set through the appropriate properties for the class.
