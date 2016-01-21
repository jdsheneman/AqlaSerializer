AqlaSerializer
==============
It is a fast and portable binary serializer designed to be easily used on your existing code with minimal changes on a wide range of .NET platforms. With AqlaSerializer you can store objects as a small in size binary data (far smaller than xml). And it's more CPU effective than BinaryFormatter and other core .NET serializers (which could be unavailable on your target platform).

Basically this is a fork of well known <a href="https://github.com/mgravell/protobuf-net">protobuf-net</a> project. Protobuf-net,  as a *data serializer*, has issues with handling some very common .NET specific features. See also <a href="https://github.com/AqlaSolutions/AqlaSerializer/wiki/Comparsion-with-protobuf-net-and-migration">comparsion page</a>.

AqlaSerializer is an *object* serializer, it's goal is not to make just a Protocol Buffers implementation but above all support important .NET features like nested collections, references, etc. There will be still a "compatible mode" to support Protocol Buffers.

It is a free open source project in which you can participiate.

Nuget: <a href="https://www.nuget.org/packages/aqlaserializer/">aqlaserializer</a>.

See also <a href="https://github.com/AqlaSolutions/AqlaSerializer/wiki">wiki</a>.

See also <a href="https://github.com/AqlaSolutions/AqlaSerializer/blob/master/Licence.txt">License.txt</a>.
