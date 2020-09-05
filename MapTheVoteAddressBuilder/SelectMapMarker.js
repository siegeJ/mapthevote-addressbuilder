var success = markerHashMap.has(arguments[0]);
if (success)
{
	google.maps.event.trigger(markerHashMap.get(arguments[0]), 'click');
	success = true;
}
return success;