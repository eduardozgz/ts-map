using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using TsMap.Helpers.Logger;
using System.Xml;

namespace TsMap
{
    public class TsMapOSMRenderer
    {
        public XmlDocument xml;
        private readonly TsMapper _mapper;
        private readonly float _scale = 0.00025f;
        private ulong _unusedId = 0;
        private List<XmlNode> wayElements;
        public TsMapOSMRenderer(TsMapper mapper)
        {
            _mapper = mapper;
            xml = new XmlDocument();
            wayElements = new List<XmlNode>();
        }

        private ulong NewId()
        {
            _unusedId++;
            return _unusedId;

        }

        public void Render(RenderFlags renderFlags = RenderFlags.All)
        {
            if (_mapper == null)
            {
                return;
            }

            XmlNode docNode = xml.CreateXmlDeclaration("1.0", "us-ascii", null);
            xml.AppendChild(docNode);

            XmlElement osmElement = xml.CreateElement("osm");
            xml.AppendChild(osmElement);

            XmlAttribute osmVersionAtt = xml.CreateAttribute("version");
            osmVersionAtt.Value = "0.6";
            osmElement.Attributes.Append(osmVersionAtt);

            XmlAttribute generatorAtt = xml.CreateAttribute("generator");
            generatorAtt.Value = "ts-map2osm";
            osmElement.Attributes.Append(generatorAtt);


            XmlElement boundsElement = xml.CreateElement("bounds");
            osmElement.AppendChild(boundsElement);

            XmlAttribute minlatAtt = xml.CreateAttribute("minlat");
            minlatAtt.Value = (-85).ToString("n7");
            boundsElement.Attributes.Append(minlatAtt);

            XmlAttribute minlonAtt = xml.CreateAttribute("minlon");
            minlonAtt.Value = (-180).ToString("n7");
            boundsElement.Attributes.Append(minlonAtt);

            XmlAttribute maxlatAtt = xml.CreateAttribute("maxlat");
            maxlatAtt.Value = (85).ToString("n7");
            boundsElement.Attributes.Append(maxlatAtt);

            XmlAttribute maxlonAtt = xml.CreateAttribute("maxlon");
            maxlonAtt.Value = (180).ToString("n7");
            boundsElement.Attributes.Append(maxlonAtt);

           
            if (renderFlags.IsActive(RenderFlags.Roads))
            {
                var roads = _mapper.Roads;

                foreach (var road in roads)
                {
                    if (road.Hidden) continue;
                    if (road.IsSecret && !renderFlags.IsActive(RenderFlags.SecretRoads))
                    {
                        continue;
                    }

                    var startNode = road.GetStartNode();
                    var endNode = road.GetEndNode();

                    if (!road.HasPoints())
                    {
                        var newPoints = new List<PointF>();

                        var sx = startNode.X;
                        var sz = startNode.Z;
                        var ex = endNode.X;
                        var ez = endNode.Z;

                        var radius = Math.Sqrt(Math.Pow(sx - ex, 2) + Math.Pow(sz - ez, 2));

                        var tanSx = Math.Cos(-(Math.PI * 0.5f - startNode.Rotation)) * radius;
                        var tanEx = Math.Cos(-(Math.PI * 0.5f - endNode.Rotation)) * radius;
                        var tanSz = Math.Sin(-(Math.PI * 0.5f - startNode.Rotation)) * radius;
                        var tanEz = Math.Sin(-(Math.PI * 0.5f - endNode.Rotation)) * radius;

                        for (var i = 0; i < 8; i++)
                        {
                            var s = i / (float)(8 - 1);
                            var x = (float)TsRoadLook.Hermite(s, sx, ex, tanSx, tanEx);
                            var z = (float)TsRoadLook.Hermite(s, sz, ez, tanSz, tanEz);
                            newPoints.Add(new PointF(x, z));
                        }
                        road.AddPoints(newPoints);
                    }


                    var type = "primary";
                    if (road.IsSecret)
                    {
                        type = "unclassified";
                    }
                    else if (
                        road.RoadLook.LanesRight.Any((lane) => { return lane.Contains("motorway"); })
                        || road.RoadLook.LanesLeft.Any((lane) => { return lane.Contains("motorway"); }))
                    {
                        type = "motorway";
                    }

 

                    XmlElement wayElement = xml.CreateElement("way");
                    wayElements.Add(wayElement);

                    XmlAttribute wayIdAtt = xml.CreateAttribute("id");
                    wayIdAtt.Value = NewId().ToString();
                    wayElement.Attributes.Append(wayIdAtt);

                    XmlElement tagElement = xml.CreateElement("tag");
                    wayElement.AppendChild(tagElement);


                    XmlAttribute kAtt = xml.CreateAttribute("k");
                    kAtt.Value = "highway";
                    tagElement.Attributes.Append(kAtt);


                    XmlAttribute vAtt = xml.CreateAttribute("v");
                    vAtt.Value = type;
                    tagElement.Attributes.Append(vAtt);

                    var roadPoints = road.GetPoints()?.ToArray();
                    for (var i = 0; i < roadPoints.Length; i++)
                    {
                        var roadPoint = roadPoints[i];
                        ulong id = road.Uid;
                        if (i == (roadPoints.Length - 1))
                        {
                            id = road.GetEndNode().Uid;
                        }
                        else if (i > 0)
                        {
                            id = NewId();
                        }

                        // Add node
                        XmlElement nodeElement = xml.CreateElement("node");
                        osmElement.AppendChild(nodeElement);

                        XmlAttribute idAtt = xml.CreateAttribute("id");
                        idAtt.Value = id.ToString();
                        nodeElement.Attributes.Append(idAtt);

                        XmlAttribute latAtt = xml.CreateAttribute("lat");
                        latAtt.Value = (-roadPoint.Y * _scale).ToString("n7");
                        nodeElement.Attributes.Append(latAtt);

                        XmlAttribute lonAtt = xml.CreateAttribute("lon");
                        lonAtt.Value = (roadPoint.X * _scale).ToString("n7"); 
                        nodeElement.Attributes.Append(lonAtt);

                        // add node to way
                        XmlElement ndElement = xml.CreateElement("nd");
                        wayElement.AppendChild(ndElement);

                        XmlAttribute refAtt = xml.CreateAttribute("ref");
                        refAtt.Value = id.ToString();
                        ndElement.Attributes.Append(refAtt);
                    }

                      
                }
            }

            if (renderFlags.IsActive(RenderFlags.Prefabs))
            {
                var prefabs = _mapper.Prefabs;

                foreach (var prefabItem in prefabs)
                {
                    if (prefabItem.Hidden) continue;
                    if (prefabItem.IsSecret && !renderFlags.IsActive(RenderFlags.SecretRoads))
                    {
                        continue;
                    }
                    var originNode = _mapper.GetNodeByUid(prefabItem.Nodes[0]);
                    if (prefabItem.Prefab.PrefabNodes == null) continue;

                    if (!prefabItem.HasLooks())
                    {
                        var mapPointOrigin = prefabItem.Prefab.PrefabNodes[prefabItem.Origin];

                        var rot = (float)(originNode.Rotation - Math.PI -
                                           Math.Atan2(mapPointOrigin.RotZ, mapPointOrigin.RotX) + Math.PI / 2);

                        var prefabstartX = originNode.X - mapPointOrigin.X;
                        var prefabStartZ = originNode.Z - mapPointOrigin.Z;

                        List<int> pointsDrawn = new List<int>();

                        for (var i = 0; i < prefabItem.Prefab.MapPoints.Count; i++)
                        {
                            var mapPoint = prefabItem.Prefab.MapPoints[i];
                            pointsDrawn.Add(i);

                            if (mapPoint.LaneCount == -1) // non-road Prefab
                            {
                                continue;
                                // TODO 
                                Dictionary<int, PointF> polyPoints = new Dictionary<int, PointF>();
                                var nextPoint = i;
                                do
                                {
                                    if (prefabItem.Prefab.MapPoints[nextPoint].Neighbours.Count == 0) break;

                                    foreach (var neighbour in prefabItem.Prefab.MapPoints[nextPoint].Neighbours)
                                    {
                                        if (!polyPoints.ContainsKey(neighbour)) // New Polygon Neighbour
                                        {
                                            nextPoint = neighbour;
                                            var newPoint = RenderHelper.RotatePoint(
                                                prefabstartX + prefabItem.Prefab.MapPoints[nextPoint].X,
                                                prefabStartZ + prefabItem.Prefab.MapPoints[nextPoint].Z, rot, originNode.X,
                                                originNode.Z);

                                            polyPoints.Add(nextPoint, new PointF(newPoint.X, newPoint.Y));
                                            break;
                                        }
                                        nextPoint = -1;
                                    }
                                } while (nextPoint != -1);

                                if (polyPoints.Count < 2) continue;

                                var colorFlag = prefabItem.Prefab.MapPoints[polyPoints.First().Key].PrefabColorFlags;

                           

                                var prefabLook = new TsPrefabPolyLook(polyPoints.Values.ToList())
                                {
                                    ZIndex = ((colorFlag & 0x01) != 0) ? 3 : 2
                                };

                                prefabItem.AddLook(prefabLook);
                                continue;
                            }

                            var mapPointLaneCount = mapPoint.LaneCount;

                            if (mapPointLaneCount == -2 && i < prefabItem.Prefab.PrefabNodes.Count)
                            {
                                if (mapPoint.ControlNodeIndex != -1) mapPointLaneCount = prefabItem.Prefab.PrefabNodes[mapPoint.ControlNodeIndex].LaneCount;
                            }

                            foreach (var neighbourPointIndex in mapPoint.Neighbours) // TODO: Fix connection between road segments
                            {
                                if (pointsDrawn.Contains(neighbourPointIndex)) continue;
                                var neighbourPoint = prefabItem.Prefab.MapPoints[neighbourPointIndex];

                                if ((mapPoint.Hidden || neighbourPoint.Hidden) && prefabItem.Prefab.PrefabNodes.Count + 1 <
                                    prefabItem.Prefab.MapPoints.Count) continue;

                                var roadYaw = Math.Atan2(neighbourPoint.Z - mapPoint.Z, neighbourPoint.X - mapPoint.X);

                                var neighbourLaneCount = neighbourPoint.LaneCount;

                                if (neighbourLaneCount == -2 && neighbourPointIndex < prefabItem.Prefab.PrefabNodes.Count)
                                {
                                    if (neighbourPoint.ControlNodeIndex != -1) neighbourLaneCount = prefabItem.Prefab.PrefabNodes[neighbourPoint.ControlNodeIndex].LaneCount;
                                }

                                if (mapPointLaneCount == -2 && neighbourLaneCount != -2) mapPointLaneCount = neighbourLaneCount;
                                else if (neighbourLaneCount == -2 && mapPointLaneCount != -2) neighbourLaneCount = mapPointLaneCount;
                                else if (mapPointLaneCount == -2 && neighbourLaneCount == -2)
                                {
                                    Logger.Instance.Debug($"Could not find lane count for ({i}, {neighbourPointIndex}), defaulting to 1 for {prefabItem.Prefab.FilePath}");
                                    mapPointLaneCount = neighbourLaneCount = 1;
                                }
                                 
                                XmlElement wayElement = xml.CreateElement("way");
                                wayElements.Add(wayElement);

                                XmlAttribute wayIdAtt = xml.CreateAttribute("id");
                                wayIdAtt.Value = NewId().ToString();
                                wayElement.Attributes.Append(wayIdAtt);

                                XmlElement tagElement = xml.CreateElement("tag");
                                wayElement.AppendChild(tagElement);


                                XmlAttribute kAtt = xml.CreateAttribute("k");
                                kAtt.Value = "highway";
                                tagElement.Attributes.Append(kAtt);

                                var roadType = "primary"; // TODO detect, maybe by node id in .Nodes?
                                if (prefabItem.IsSecret)
                                {
                                    roadType = "unclassified";
                                }

                                XmlAttribute vAtt = xml.CreateAttribute("v");
                                vAtt.Value = roadType; 
                                tagElement.Attributes.Append(vAtt);

                                PointF[] nodes = {
                                    RenderHelper.RotatePoint(prefabstartX + mapPoint.X, prefabStartZ + mapPoint.Z, rot, originNode.X, originNode.Z),
                                    RenderHelper.RotatePoint(prefabstartX + neighbourPoint.X, prefabStartZ + neighbourPoint.Z, rot, originNode.X, originNode.Z)
                                };

                                foreach (PointF node in nodes)
                                {
                                    ulong id;

                                    if (neighbourPointIndex < prefabItem.Nodes.Count)
                                    {
                                        id = prefabItem.Nodes[neighbourPointIndex];
                                    } else
                                    {
                                        id = NewId();

                                    }

                                    XmlElement nodeElement = xml.CreateElement("node");
                                    osmElement.AppendChild(nodeElement);

                                    XmlAttribute idAtt = xml.CreateAttribute("id");
                                    idAtt.Value = id.ToString();
                                    nodeElement.Attributes.Append(idAtt);

                                    XmlAttribute latAtt = xml.CreateAttribute("lat");
                                    latAtt.Value = (-node.Y * _scale).ToString("n7");
                                    nodeElement.Attributes.Append(latAtt);

                                    XmlAttribute lonAtt = xml.CreateAttribute("lon");
                                    lonAtt.Value = (node.X * _scale).ToString("n7");
                                    nodeElement.Attributes.Append(lonAtt);

                                    // add node to way
                                    XmlElement ndElement = xml.CreateElement("nd");
                                    wayElement.AppendChild(ndElement);

                                    XmlAttribute refAtt = xml.CreateAttribute("ref");
                                    refAtt.Value = id.ToString();
                                    ndElement.Attributes.Append(refAtt);
                                }
                            }
                        }
                    }

                }

            }

            // TODO connect ways to each other for OSRM


            // ways must be defined after the nodes
            foreach (XmlNode wayNode in wayElements)
            {
                osmElement.AppendChild(wayNode);
            }
        }
    }
}
