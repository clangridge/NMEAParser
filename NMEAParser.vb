﻿Imports Esri.ArcGISRuntime.Location
Imports Esri.ArcGISRuntime.Geometry
Imports System.IO.Ports
Imports System.Timers

''' <summary>
''' Class for converting NMEA GPS data supplied through a Serial COM Port into a format that is usable by ESRI's runtime library.
''' Notes about usage: When calling the StopAsync function, you must either set up an event handler to listen for the HasClosed event in this class or else Await the Task associated with the StopAsync Function.  Until either of these occurs, the user cannot be allowed to restart the application or they can lock the application.  This is due to the resources that the serial port is using being on another thread, so the application needs to wait for that other thread to complete to be sure all the resources have been released and it is safe to reconnect to the com port.
''' You must also wait for the HasClosed event to fire or the Task from OnStop to complete when closing the form that originally owned the class.  If you don't, the form will not be able to close as it will still be receiving updates from the class, which will block the form from closing.
''' 
''' It should be noted that in testing, using the Event path worked better with ESRI's MapView.LocationDisplay functionality.  Directly calling the StopAsync doesn't properly disconnect the data supply from the map, so even though you have called it, locations still are displayed on the map.
''' 
''' Clark Langridge, New Brunswick Dept. of Energy and Resource Development, September 2017
''' </summary>
Public Class NMEAParser
    Inherits LocationDataSource
    Implements IDisposable

#Region "Private Variables"
    Private WithEvents _serialPort As SerialPort = Nothing
    Private _canRead As Boolean = False
    Private _comPort As String
    Private _baudRate As Integer
    Private _location As MapPoint = Nothing
    'Private _utcTime As Double = 0
    Private _numSatellites As Integer = 0
    Private _fixQuality As FixQuality = FixQuality.Invalid
    Private _hdop As Double = Double.PositiveInfinity
    Private _velocity As Double = 0
    Private _heading As Double = 0
    'Private _havePosition As Boolean = False
    Private WithEvents _timeoutTimer As New Timer
    Private _closeTask As TaskCompletionSource(Of Boolean)
    Private _timeOutInterval As Integer = 5000
    'Private _formClosing As Boolean = False

    Public Event HasClosed()

    Public Enum FixQuality As Integer
        Invalid = 0
        GPS_Fix = 1
        DGPS_Fix = 2
        PPS_Fix = 3
        RTK = 4
        RTK_Float = 5
        Estimated = 6
        Manual_Input = 7
        Simulation = 8

    End Enum

#End Region

    Public Sub New()
        'MyBase.New()

    End Sub

    Protected Overrides Function OnStartAsync() As Task
        Try
            If _serialPort Is Nothing Then
                _serialPort = New SerialPort(COM_PORT, BAUD_RATE)

            End If


            _serialPort.ReadTimeout = SerialPort.InfiniteTimeout
            _timeoutTimer.Interval = TIMEOUT_INTERNAL


            Dim startAction As Action = AddressOf _serialPort.Open
            '_serialPort.Open()
            '_receptionTimer.Start()
            _canRead = True
            _timeoutTimer.Start()
            Return Task.Run(startAction)

        Catch ex As Exception
            
            Return Nothing

        End Try

    End Function

    Protected Overrides Function OnStopAsync() As Task

        Try
            _canRead = False
            _timeoutTimer.Stop()

            Dim taskList As New List(Of Task)(2)
            'Creates a Task to be completed when the Serial COM Port has finished closing
            _closeTask = New TaskCompletionSource(Of Boolean)
            taskList.Add(_closeTask.Task)

            'Need to convert the Close sub to an action or else the Function hangs on the call to the sub.
            Dim stopAction As Action = AddressOf _serialPort.Close
            taskList.Add(Task.Run(stopAction))

            Return Task.WhenAll(taskList.ToArray())

        Catch ex As Exception

            Return Nothing
        End Try

    End Function

#Region "Events"

    ''' <summary>
    ''' Need this because closing the port requires the release of resources that are not on this thread, so no way of predicting when that occurs.
    ''' </summary>
    ''' <param name="sender"></param>
    ''' <param name="e"></param>
    Private Sub _serialPort_Disposed(sender As Object, e As EventArgs) Handles _serialPort.Disposed
        Try
            _serialPort = Nothing
            RaiseEvent HasClosed()
            _closeTask.TrySetResult(True)

        Catch ex As Exception

            _closeTask.TrySetException(ex)

        End Try
    End Sub


    Private Sub _serialPort_DataReceived(sender As Object, e As SerialDataReceivedEventArgs) Handles _serialPort.DataReceived
        Try
            If _canRead Then
                Dim msg As String = _serialPort.ReadTo(vbCrLf)

                If CheckNMEACheckSum(msg) Then
                    ParseNMEA(msg)
            
                End If

            End If

        Catch ex As Exception

            NEW_LOCATION(False)

        End Try
    End Sub

    Private Sub _serialPort_ErrorReceived(sender As Object, e As SerialErrorReceivedEventArgs) Handles _serialPort.ErrorReceived

        If _location IsNot Nothing Then
                NEW_LOCATION(False)

            End If

    End Sub

    Private Sub _timeoutTimer_Elapsed(sender As Object, e As ElapsedEventArgs) Handles _timeoutTimer.Elapsed

        If _location IsNot Nothing Then
                NEW_LOCATION(False)

            End If


    End Sub

#End Region

#Region "Subs/Functions"
    ''' <summary>
    ''' Takes the supplied string and checks if against the checksum included on the end of the string.
    ''' </summary>
    ''' <param name="sentence">The NMEA sentence to be checked</param>
    ''' <returns>True if the checksum corresponds to the value found for the string.  False o.w.</returns>
    Private Function CheckNMEACheckSum(ByVal sentence As String) As Boolean
        Try
            '* used to flag the start of the checksum.  If can't find, the string is bad and should be ignored
            If sentence.IndexOf("*") <= 0 Then
                Return False

            End If

            Dim NMEAchecksum() As String = sentence.Split(Convert.ToChar("*"))
            NMEAchecksum(0) = NMEAchecksum(0).Trim
            NMEAchecksum(1) = NMEAchecksum(1).Trim

            'Derive the value for the checksum in bytes
            Dim checksum As Integer = 0
            For Each entry As Char In NMEAchecksum(0)

                If entry <> "$" Then
                    checksum = checksum Xor Convert.ToByte(entry)
                End If
            Next

            'Convert checksum to hexidecimal format
            Dim checksumHex As String = String.Format("{0:X}", checksum)


            Return NMEAchecksum(1) = checksumHex

        Catch ex As Exception
            
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Takes the input sentence and parses it to derive GPS data for use.  Not all the available NMEA sentences are handled since many of them deal with information that ESRI's location data service cannot handle.  For those sentences, the data is ignored.
    ''' The checksum for the sentence should have already been checked before this sub is called
    ''' </summary>
    ''' <param name="sentence">The NMEA sentence to be parsed</param>
    Private Sub ParseNMEA(ByVal sentence As String)
        'Try
        If sentence Is Nothing Then
                Exit Sub
            End If

            Dim sentenceArray() As String = sentence.Split(Convert.ToChar(","))

            If sentenceArray(0).Contains("GGA") Then
          
                SET_NUM_SATELLITES = sentenceArray(7)
                SET_HDOP = sentenceArray(8)
                SetLocation(sentenceArray(2), sentenceArray(3), sentenceArray(4), sentenceArray(5))
                SET_FIX_QUALITY = sentenceArray(6)

            If HAVE_POSITION Then
                _timeoutTimer.Stop()
                NEW_LOCATION(True)
                _timeoutTimer.Start()

            End If

        ElseIf sentenceArray(0).Contains("RMC") Then
                SET_VELOCITY(True) = sentenceArray(7)
                SET_HEADING = sentenceArray(8)


        ElseIf sentenceArray(0).Contains("VTG") Then
                SET_HEADING = sentenceArray(1)
                SET_VELOCITY(False) = sentenceArray(7)

        End If


    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
            If IsStarted Then
                StopAsync()

            End If

    End Sub

    ''' <summary>
    ''' Takes the string format and returns a representation of the value as a Double
    ''' </summary>
    ''' <param name="coordinate">The string representation of the point to be parsed  In NMEA strings, the data is formatted as degrees decimal minutes with no space between the degrees and minutes sections</param>
    ''' <param name="hemisphere">The hemisphere of the value, North, South, East or West.  This is a seperate entry in the string and is used to determine the sign of the final value</param>
    ''' <returns>The value of the coordinate as a Double.  If an issue occurs while determining the value, a value of NaN is returned</returns>
    Private Function ParseCoordinates(ByVal coordinate As String, ByVal hemisphere As String) As Double
        Try
            'The minutes section always starts 2 characters before the decimal place.  If the decimal can't be found, then there is an issue with the string and should be ignored
            Dim index As Integer = coordinate.IndexOf(".")
            If index = -1 Then
                Return Double.NaN

            Else
                index -= 2

            End If

            hemisphere = hemisphere.ToUpper

            'Extract the appropriate substrings from the supplied string and convert to decimal degrees
            Dim degrees As Double = Convert.ToDouble(coordinate.Substring(0, index))
            Dim minutes As Double = Convert.ToDouble(coordinate.Substring(index))

            Dim decimalDegrees As Double = degrees + minutes / 60

            'Check hemisphere value to see whether it should be positive or negative
            If hemisphere = "W" OrElse hemisphere = "S" Then
                decimalDegrees *= -1

            End If

            Return decimalDegrees


        Catch ex As Exception

            Return Double.NaN
        End Try
    End Function

    ''' <summary>
    ''' Internal sub for creating the geometry needed for defining the location on the map.  If a value position is found, then the _havePosition flag is set to True, otherwise set to False
    ''' </summary>
    ''' <param name="latitude">The latitude value extracted from the NMEA string</param>
    ''' <param name="lat_hemisphere">The hemisphere for the latitude extracted from the NMEA string</param>
    ''' <param name="longitude">The longitude value extracted from the NMEA string</param>
    ''' <param name="long_hemisphere">The hemisphere for the longitude extracted from the NMEA string</param>
    Private Sub SetLocation(ByVal latitude As String, ByVal lat_hemisphere As String, ByVal longitude As String, ByVal long_hemisphere As String)
        Try
            Dim longComponent As Double = ParseCoordinates(longitude, long_hemisphere)
            Dim latComponent As Double = ParseCoordinates(latitude, lat_hemisphere)

            If longComponent = Double.NaN OrElse latComponent = Double.NaN Then
                
                _fixQuality = FixQuality.Invalid

            Else
                If NUM_SATELLITES >= 4 Then
                    _location = New MapPoint(longComponent, latComponent, SpatialReferences.Wgs84)

                End If

                _fixQuality = FixQuality.Estimated
                    
                End If

        Catch ex As Exception

            _fixQuality = FixQuality.Invalid
        End Try
    End Sub

#End Region


#Region "Properties"

    Public Shadows ReadOnly Property IsStarted As Boolean
        Get
            If _serialPort Is Nothing Then
                Return False

            Else
                Return _serialPort.IsOpen 

            End If

        End Get
    End Property

    ''' <summary>
    ''' Gets or sets the COM Port the GPS is connected to
    ''' </summary>
    ''' <returns></returns>
    Public Property COM_PORT As String
        Get
            Return _comPort

        End Get

        Set(value As String)
            _comPort = value

        End Set
    End Property

    ''' <summary>
    ''' Gets or Sets the Baud Rate that the serial COM port is receiving data at
    ''' </summary>
    ''' <returns></returns>
    Public Property BAUD_RATE As Integer
        Get
            Return _baudRate

        End Get

        Set(value As Integer)
            _baudRate = value

        End Set
    End Property

    ''' <summary>
    ''' Gets or Sets the the time-out interval, in milliseconds for the GPS.  If the time interval passes without an update to the user's position, the location information is updated to reflect the stale status of the position.  By default, the value is 5000 milliseconds (5 seconds).
    ''' </summary>
    ''' <returns></returns>
    Public Property TIMEOUT_INTERNAL As Integer
        Get
            Return _timeOutInterval

        End Get
        Set(value As Integer)
            _timeOutInterval = value

        End Set
    End Property



    ''' <summary>
    ''' Sets the number of satellites that are currently visible to the receiver
    ''' </summary>
    Protected WriteOnly Property SET_NUM_SATELLITES As String
        Set(value As String)
            If IsNumeric(value) Then
                _numSatellites = Convert.ToInt32(value)
            End If

        End Set
    End Property

    ''' <summary>
    ''' The number of satellites current being used by the receiver to resolve its position
    ''' </summary>
    ''' <returns></returns>
    Public ReadOnly Property NUM_SATELLITES As Integer
        Get
            Return _numSatellites

        End Get
    End Property

    ''' <summary>
    ''' Sets the fix quality observed by the GPS
    ''' </summary>
    Protected WriteOnly Property SET_FIX_QUALITY As String
        Set(value As String)
            If IsNumeric(value) Then
                _fixQuality = CType(value, FixQuality)

            End If
        End Set
    End Property

    ''' <summary>
    ''' Sets the HDOP value determined for the current satellite configuration
    ''' </summary>
    Protected WriteOnly Property SET_HDOP As String
        Set(value As String)
            If IsNumeric(value) Then
                _hdop = Convert.ToDouble(value)

            End If
        End Set
    End Property


    ''' <summary>
    ''' Sets the observed speed for the GPS unit
    ''' When assigning values to the property, assumes that the input is either km/h or knots.  Value is converted to m/s.
    ''' </summary>
    ''' <param name="isKnots">Flag indicating whether a speed is in knots or not.  If True, input is in assumed to be in knots, otherwise the value is assumed to in km/h.</param>
    Protected WriteOnly Property SET_VELOCITY(ByVal isKnots As Boolean) As String

        Set(value As String)
            If IsNumeric(value) Then
                Dim tempValue As Double = Convert.ToDouble(value)
                If isKnots Then
                    _velocity = tempValue * 0.514

                Else
                    _velocity = tempValue / 3.6

                End If
            End If

        End Set
    End Property

    ''' <summary>
    ''' Sets the current heading for the GPS unit
    ''' </summary>
    Protected WriteOnly Property SET_HEADING As String
        Set(value As String)
            If IsNumeric(value) Then
                _heading = Convert.ToDouble(value)

                If HAVE_POSITION Then
                    MyBase.UpdateHeading(_heading)

                End If
            End If

        End Set
    End Property


    ''' <summary>
    ''' Updates the Location object based on the available GPS data and pushes it into the Base Class' UpdateLocation sub
    ''' </summary>
    ''' <param name="New_Position">Flag to indicate whether the location should be treated as a new position or not.  A value of True indicates that it is a new position, False indicates otherwise (i.e., a stale location based on old GPS data)</param>

    Protected Sub NEW_LOCATION(ByVal New_Position As Boolean) 'As Location

        Dim current_accuracy As Double = 56556

        If New_Position Then
            Select Case _fixQuality
                Case FixQuality.GPS_Fix
                    '1-sigma confidence interval
                    current_accuracy = 5.102 * _hdop

                Case FixQuality.DGPS_Fix
                    'Assumes WAAS at 1-sigma confidence interval
                    current_accuracy = 3.878 * _hdop

                Case FixQuality.RTK, FixQuality.RTK_Float, FixQuality.PPS_Fix
                    current_accuracy = 0.1 * _hdop


            End Select
        Else
            _velocity = 0

        End If

        If _location IsNot Nothing Then
            MyBase.UpdateLocation(New Location(_location, current_accuracy, _velocity, _heading, Not New_Position))

        End If

    End Sub

    ''' <summary>
    ''' Gets property indicating whether the current GPS data should be considered valid or not
    ''' </summary>
    ''' <returns>True if the number of satellites is 4 or greater and the fix quality is not invalid or estimated</returns>
    Public ReadOnly Property HAVE_POSITION As Boolean
        Get
            Return NUM_SATELLITES >= 4 AndAlso Not (_fixQuality = FixQuality.Invalid OrElse _fixQuality = FixQuality.Estimated)
        End Get
    End Property

#End Region
End Class

