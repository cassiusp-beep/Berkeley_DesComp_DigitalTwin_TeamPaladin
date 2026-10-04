import unittest
from types import SimpleNamespace
from bridge import PerformerTracker, ZoneTracker, body_size, extract_features, make_tracker, requested_count


class TrackingTests(unittest.TestCase):
    def test_roles_survive_detection_reordering_and_dropout(self):
        t = PerformerTracker(3)
        self.assertEqual(t.assign([(.8, .5), (.2, .5)]), {})
        self.assertEqual(t.assign([(.8, .5), (.2, .5), (.5, .5)]), {1: 1, 2: 2, 3: 0})
        self.assertEqual(t.assign([(.51, .5), (.81, .5), (.21, .5)]), {1: 2, 2: 0, 3: 1})
        self.assertEqual(t.assign([(.52, .5), (.22, .5)]), {1: 1, 2: 0})
        self.assertEqual(t.assign([(.82, .5)]), {3: 0})

    def test_ambiguous_assignment_is_rejected(self):
        t = PerformerTracker(3)
        t.assign([(.2, .5), (.4, .5), (.8, .5)])
        self.assertEqual(t.assign([(.3, .5), (.8, .5)]), {})
        t.reset()
        self.assertIsNone(t.positions)

    def test_one_and_two_people_with_extra_bystanders(self):
        for count in (1, 2):
            t = PerformerTracker(count)
            assigned = t.assign([(.8, .5), (.2, .5), (.5, .5)])
            self.assertEqual(len(assigned), count)
            self.assertEqual(assigned[1], 1)
            self.assertEqual(t.assign([(.21, .5)]), {1: 0})

    def test_count_request_validation(self):
        for count in (1, 2, 3, 4):
            self.assertEqual(requested_count('{"version":1,"performerCount":%d}' % count), count)
        for data in ('null', '[]', 'bad', '{"version":1,"performerCount":0}',
                     '{"version":1,"performerCount":5}', '{"version":1,"performerCount":true}',
                     '{"version":1,"performerCount":"2"}', '{"version":2,"performerCount":2}'):
            self.assertIsNone(requested_count(data))

    def test_four_people(self):
        t = PerformerTracker(4)
        self.assertEqual(len(t.assign([(.1, .5), (.35, .5), (.6, .5), (.85, .5)])), 4)
        self.assertEqual(t.assign([]), {})

    def test_upper_body_works_with_no_visible_lower_body(self):
        points = [SimpleNamespace(x=.5, y=.5, presence=0., visibility=0.) for _ in range(33)]
        for i, xy in {11:(.4,.3), 12:(.6,.3), 15:(.3,.5), 16:(.7,.5)}.items():
            points[i] = SimpleNamespace(x=xy[0], y=xy[1], presence=1., visibility=1.)
        center, baseline, confidence = extract_features(points, True)
        self.assertEqual(center, (.5, .3))
        self.assertEqual(confidence, [1.] * 6)
        self.assertEqual(extract_features(points)[2][4], 0.)
        points[15].y -= .1
        _, raised, _ = extract_features(points, True)
        self.assertGreater(raised[0], baseline[0])
        self.assertEqual(raised[0], raised[2])
        self.assertEqual(raised[1], raised[3])
        points[11].y -= .05
        self.assertLess(extract_features(points, True)[1][4], baseline[4])
        points[15].visibility = 0.
        _, _, confidence = extract_features(points, True)
        self.assertEqual(confidence, [0., 1., 0., 1., 1., 0.])

    def test_features_are_translation_and_scale_invariant(self):
        points = [SimpleNamespace(x=.5, y=.5, presence=1., visibility=1.) for _ in range(33)]
        for i, xy in {11:(.4,.3),12:(.6,.3),23:(.4,.6),24:(.6,.6),15:(.2,.1),16:(.8,.2),27:(.4,.9),28:(.6,.9)}.items():
            points[i].x, points[i].y = xy
        _, first, _ = extract_features(points)
        for p in points:
            p.x = p.x * .5 + .1
            p.y = p.y * .5 + .2
        _, second, _ = extract_features(points)
        for a, b in zip(first, second):
            self.assertAlmostEqual(a, b)
        points[15].visibility = .1
        _, _, confidence = extract_features(points)
        self.assertEqual(confidence[0], .1)
        self.assertEqual(confidence[1], 1.)


class ZoneTrackingTests(unittest.TestCase):
    """Fixed formation: P1..PN by left-to-right image bands, with a size gate for close-up extras."""
    PERFORMER = .12  # torso length in image units for someone standing ~4 m back

    def test_zones_assign_left_to_right_in_any_detection_order(self):
        t = ZoneTracker(3)
        self.assertEqual(t.assign([(.85, .6), (.15, .6), (.5, .6)], [self.PERFORMER] * 3), {1: 1, 2: 2, 3: 0})
        self.assertEqual(t.assign([(.52, .6), (.16, .6), (.83, .6)], [self.PERFORMER] * 3), {1: 1, 2: 0, 3: 2})

    def test_waits_until_every_zone_is_filled_and_reset_clears(self):
        t = ZoneTracker(3)
        self.assertEqual(t.assign([(.15, .6), (.5, .6)], [self.PERFORMER] * 2), {})
        self.assertIsNone(t.positions)
        t.assign([(.15, .6), (.5, .6), (.85, .6)], [self.PERFORMER] * 3)
        self.assertIsNotNone(t.positions)
        t.reset()
        self.assertIsNone(t.positions)

    def test_close_up_clicker_is_ignored_at_lock_and_after(self):
        t = ZoneTracker(3)
        # Someone at the laptop sits right in front of the lens: same zone as P2, about 4x larger.
        centers = [(.15, .6), (.48, .9), (.52, .6), (.85, .6)]
        sizes = [self.PERFORMER, .5, self.PERFORMER, self.PERFORMER]
        self.assertEqual(t.assign(centers, sizes), {1: 0, 2: 2, 3: 3})
        # P2 steps away; the clicker alone in P2's zone must not take the slot.
        self.assertEqual(t.assign([(.15, .6), (.5, .85), (.85, .6)], [self.PERFORMER, .5, self.PERFORMER]), {1: 0, 3: 2})

    def test_neighbour_leaning_in_does_not_steal_a_slot(self):
        t = ZoneTracker(3)
        t.assign([(.15, .6), (.5, .6), (.85, .6)], [self.PERFORMER] * 3)
        # P1 is out of frame; P2's hips drift close to the boundary but stay in P2's band.
        self.assertEqual(t.assign([(.36, .6), (.85, .6)], [self.PERFORMER] * 2), {2: 0, 3: 1})

    def test_two_people_in_one_zone_nearest_wins_and_ties_are_skipped(self):
        t = ZoneTracker(3)
        t.assign([(.15, .6), (.5, .6), (.85, .6)], [self.PERFORMER] * 3)
        self.assertEqual(t.assign([(.15, .6), (.5, .6), (.6, .6), (.85, .6)], [self.PERFORMER] * 4), {1: 0, 2: 1, 3: 3})
        self.assertEqual(t.assign([(.15, .6), (.45, .6), (.55, .6), (.85, .6)], [self.PERFORMER] * 4), {1: 0, 3: 3})

    def test_one_person_prefers_the_performer_over_the_clicker(self):
        t = ZoneTracker(1)
        self.assertEqual(t.assign([(.5, .9), (.45, .6)], [.5, self.PERFORMER]), {1: 1})
        self.assertEqual(t.assign([(.5, .9), (.46, .6)], [.5, self.PERFORMER]), {1: 1})

    def test_four_people_and_mode_switch(self):
        t = make_tracker(4, 'zones')
        self.assertIsInstance(t, ZoneTracker)
        self.assertEqual(t.assign([(.1, .6), (.35, .6), (.6, .6), (.9, .6)], [self.PERFORMER] * 4), {1: 0, 2: 1, 3: 2, 4: 3})
        self.assertIsInstance(make_tracker(3, 'track'), PerformerTracker)
        self.assertEqual(make_tracker(3, 'track').assign([(.8, .5), (.2, .5), (.5, .5)], [1, 1, 1]), {1: 1, 2: 2, 3: 0})

    def test_body_size_is_torso_or_shoulder_width(self):
        pts = [SimpleNamespace(x=.5, y=.5, visibility=1, presence=1) for _ in range(33)]
        for i, (x, y) in {11: (.45, .4), 12: (.55, .4), 23: (.46, .55), 24: (.54, .55)}.items():
            pts[i] = SimpleNamespace(x=x, y=y, visibility=1, presence=1)
        self.assertAlmostEqual(body_size(pts), .15)
        self.assertAlmostEqual(body_size(pts, upper_body_only=True), .10)


if __name__ == '__main__':
    unittest.main()
