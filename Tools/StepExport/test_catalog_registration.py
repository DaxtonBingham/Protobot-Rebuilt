"""Offline CAD-build regressions; these dependencies are not shipped at runtime."""
import unittest
import numpy as np
from catalog_recipes import rigid_catalog_matrix
from kernel import transform,children,TopAbs_FACE
from OCP.BRepPrimAPI import BRepPrimAPI_MakeCylinder
from OCP.BRepAdaptor import BRepAdaptor_Surface
from OCP.GeomAbs import GeomAbs_Cylinder
from OCP.TopoDS import TopoDS

class CatalogRegistrationTests(unittest.TestCase):
    def test_float_rotation_retains_analytic_cylinder(self):
        original=np.array([[.7071068,0,.7071068,1234.5],[0,1,0,67],[-.7071068,0,.7071068,-987],[0,0,0,1]])
        matrix=np.array(rigid_catalog_matrix(original)).reshape(4,4)
        np.testing.assert_allclose(matrix[:3,:3].T@matrix[:3,:3],np.eye(3),atol=1e-12)
        np.testing.assert_array_equal(matrix[:3,3],original[:3,3])
        shape=transform(BRepPrimAPI_MakeCylinder(5,20).Shape(),matrix.reshape(-1).tolist())
        self.assertEqual(sum(BRepAdaptor_Surface(TopoDS.Face_s(f)).GetType()==GeomAbs_Cylinder for f in children(shape,TopAbs_FACE)),1)

    def test_reflection_is_preserved(self):
        matrix=np.diag([-1.00000003,1.,1.,1.])
        corrected=np.array(rigid_catalog_matrix(matrix)).reshape(4,4)
        self.assertAlmostEqual(np.linalg.det(corrected[:3,:3]),-1.)

    def test_actual_scale_and_shear_require_review(self):
        for matrix in (np.diag([1.01,1,1,1]),np.array([[1,.001,0,0],[0,1,0,0],[0,0,1,0],[0,0,0,1]])):
            with self.assertRaisesRegex(ValueError,'scale or shear'):rigid_catalog_matrix(matrix)

if __name__=='__main__':unittest.main()
